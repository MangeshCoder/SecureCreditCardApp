using System.Globalization;
using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Application.Features.Otp;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Transactions;

public interface ITransactionService
{
    Task<SwipeResponse> SwipeAsync(SwipeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Module 5: authorization requested by a verified partner bank through the signed, encrypted gateway.
    /// The partner may present any card (it is the merchant's bank), and its request signature is stored
    /// on the ledger row (Transactions.DigitalSignature).
    /// </summary>
    Task<SwipeResponse> AuthorizeFromGatewayAsync(SwipeRequest request, string partnerSignature, CancellationToken ct = default);
    Task<BalanceChangeResponse> LoadAsync(LoadRequest request, CancellationToken ct = default);
    Task<BalanceChangeResponse> RefundAsync(int transactionId, CancellationToken ct = default);
    Task<PagedResult<TransactionDto>> GetCardTransactionsAsync(int cardId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<TransactionDto>> GetAllTransactionsAsync(int page, int pageSize, CancellationToken ct = default);
    IReadOnlyList<MerchantCategoryDto> GetMerchantCategories();
}

/// <summary>
/// Merchant swipe authorization, balance loads (repayments) and refunds.
///
/// Every operation that changes a balance:
///   1. reads the card,
///   2. applies the domain rule (Debit/Credit),
///   3. adds the ledger row,
///   4. saves both in ONE database transaction.
/// CreditCards.AvailableBalance is an optimistic-concurrency token, so if two swipes hit the same card
/// at the same moment the second save fails instead of overspending; we then retry with fresh data.
/// </summary>
public class TransactionService : ITransactionService
{
    private readonly ICreditCardRepository _cards;
    private readonly ITransactionRepository _transactions;
    private readonly ICashbackRepository _cashback;
    private readonly ICashbackEngine _cashbackEngine;
    private readonly IEmiPlanRepository _emiPlans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICardLookupHasher _lookupHasher;
    private readonly ISecretHasher _secretHasher;
    private readonly ICurrentUser _currentUser;
    private readonly ICardControlRules _controlRules;
    private readonly IStepUpAuthenticator _stepUp;
    private readonly INotifier _notifier;
    private readonly BillingCalculator _billing;
    private readonly IValidator<SwipeRequest> _swipeValidator;
    private readonly IValidator<LoadRequest> _loadValidator;

    public TransactionService(ICreditCardRepository cards, ITransactionRepository transactions,
                              ICashbackRepository cashback, ICashbackEngine cashbackEngine,
                              IEmiPlanRepository emiPlans, IUnitOfWork unitOfWork,
                              ICardLookupHasher lookupHasher, ISecretHasher secretHasher, ICurrentUser currentUser,
                              ICardControlRules controlRules, IStepUpAuthenticator stepUp, INotifier notifier,
                              BillingCalculator billing,
                              IValidator<SwipeRequest> swipeValidator, IValidator<LoadRequest> loadValidator)
    {
        _cards = cards;
        _transactions = transactions;
        _cashback = cashback;
        _cashbackEngine = cashbackEngine;
        _emiPlans = emiPlans;
        _unitOfWork = unitOfWork;
        _lookupHasher = lookupHasher;
        _secretHasher = secretHasher;
        _currentUser = currentUser;
        _controlRules = controlRules;
        _stepUp = stepUp;
        _notifier = notifier;
        _billing = billing;
        _swipeValidator = swipeValidator;
        _loadValidator = loadValidator;
    }

    // ---- Swipe (authorization) ------------------------------------------------------------

    public async Task<SwipeResponse> SwipeAsync(SwipeRequest request, CancellationToken ct = default)
    {
        await _swipeValidator.ValidateAndThrowAsync(request, ct);
        return await WithConcurrencyRetryAsync(() => AuthorizeAsync(request, SwipeSource.Simulator, ct));
    }

    public async Task<SwipeResponse> AuthorizeFromGatewayAsync(SwipeRequest request, string partnerSignature, CancellationToken ct = default)
    {
        await _swipeValidator.ValidateAndThrowAsync(request, ct);
        return await WithConcurrencyRetryAsync(() => AuthorizeAsync(request, SwipeSource.Gateway(partnerSignature), ct));
    }

    /// <summary>
    /// Who sends the swipe: the in-app simulator (JWT user) or a verified partner bank.
    /// (Not to be confused with the Module 6 TransactionChannel: HOW the card is used - shop, online, tap, ATM.)
    /// </summary>
    private sealed record SwipeSource(bool AnyCard, string? PartnerSignature, bool AsksForOnlineOtp)
    {
        public static readonly SwipeSource Simulator = new(false, null, true);
        // A partner bank authenticates the cardholder itself (3-D Secure) before it calls the gateway.
        public static SwipeSource Gateway(string signature) => new(true, signature, false);
    }

    private async Task<SwipeResponse> AuthorizeAsync(SwipeRequest r, SwipeSource source, CancellationToken ct)
    {
        // Module 6: how and where the card is used - decides which of the cardholder's controls apply.
        var origin = _controlRules.OriginOf(r);

        // 1. Find the card through the blind index - the number itself is never searched or logged.
        var card = await _cards.GetByNumberHashAsync(_lookupHasher.Compute(r.CardNumber), ct);

        // Cardholders use the simulator with their own cards only; admins and verified partner banks act as
        // the merchant terminal. An unknown or foreign card cannot be linked to a ledger row, so nothing is recorded.
        // (AnyCard is checked first: a gateway call has no logged-in user, so UserId must not be read.)
        if (card is null || (!source.AnyCard && !_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            return Declined(null, null, r.Amount, DeclineReasons.InvalidCardDetails);

        // 2. Card-not-present data: expiry date and CVV. Same generic reason for both on purpose.
        bool expiryMatches = card.ExpiryDate.Month == r.ExpiryMonth && card.ExpiryDate.Year == r.ExpiryYear;
        if (!expiryMatches || !_secretHasher.Verify(r.Cvv, card.CvvHash))
            return await RecordDeclineAsync(card, r, origin, source, DeclineReasons.InvalidCardDetails, ct);

        // 3. Card state. Checked before the PIN so a blocked card cannot be used to guess PINs.
        if (!card.IsActive) return await RecordDeclineAsync(card, r, origin, source, DeclineReasons.CardBlocked, ct);
        if (card.IsExpired) return await RecordDeclineAsync(card, r, origin, source, DeclineReasons.CardExpired, ct);

        // 3b. Module 6: the cardholder's switches - temporary lock, channel, international use.
        //     Also before the PIN: a locked card or a switched-off channel cannot be used to guess PINs.
        var usage = card.GetUsageDeclineReason(origin.Channel, origin.IsInternational);
        if (usage is not null) return await RecordDeclineAsync(card, r, origin, source, usage, ct);

        // 4. PIN with 3-strikes lockout.
        var pin = PinCheck.Verify(_secretHasher, card, r.Pin);
        if (pin == PinCheckResult.LockedOut) return await RecordDeclineAsync(card, r, origin, source, DeclineReasons.PinTriesExceeded, ct);
        if (pin == PinCheckResult.Invalid) return await RecordDeclineAsync(card, r, origin, source, DeclineReasons.IncorrectPin, ct);

        // 5. Module 6: the cardholder's daily limits and the bank's contactless cap.
        //    Race-safe: two swipes at the same moment both change AvailableBalance (a concurrency token),
        //    so the second one is retried and then sees the first one in today's spend.
        var spentToday = await _transactions.GetApprovedSpendAsync(card.CardId, _controlRules.StartOfTodayUtc(), ct);
        var limit = card.EnsureControls().GetLimitDeclineReason(origin.Channel, origin.IsInternational, r.Amount,
                                                               spentToday, _controlRules.Options.ContactlessPerTransactionLimit);
        if (limit is not null) return await RecordDeclineAsync(card, r, origin, source, limit, ct);

        // 6. Funds. Module 8: cash comes with a fee (+ GST) charged right away, so the cash AND the fee must fit.
        var cashFee = origin.Channel == TransactionChannel.Atm ? _billing.CashAdvanceFee(r.Amount) : 0m;
        var cashFeeGst = _billing.Gst(cashFee);
        var reason = card.GetSwipeDeclineReason(r.Amount + cashFee + cashFeeGst);
        if (reason is not null) return await RecordDeclineAsync(card, r, origin, source, reason, ct);

        // 7. Module 7: an online purchase needs a one-time code sent to the cardholder's phone (RBI "additional
        //    factor of authentication"; 3-D Secure in card networks). The code is bound to this card, amount and
        //    merchant, so it cannot approve a different payment. Asked last: no SMS for a payment that fails anyway.
        if (origin.Channel == TransactionChannel.Online && source.AsksForOnlineOtp)
        {
            var merchant = r.MerchantName.Trim();
            await _stepUp.RequireAsync(new StepUpRequest(card.CardholderId, OtpPurpose.OnlinePayment,
                $"card={card.CardId}|amount={r.Amount.ToString("0.00", CultureInfo.InvariantCulture)}|merchant={merchant}",
                $"to pay {Alerts.Money(r.Amount)} at {merchant} with {Alerts.CardName(card)}"), ct);
        }

        // 8. Approve: debit + ledger row ...
        card.Debit(r.Amount);
        var txn = CardTransaction.ApprovedSwipe(card.CardId, r.MerchantName, r.MerchantCategoryCode, r.Amount, origin);
        if (source.PartnerSignature is not null) txn.AttachDigitalSignature(source.PartnerSignature);
        await _transactions.AddAsync(txn, ct);
        if (cashFee > 0)
        {
            var now = DateTime.UtcNow;
            card.ApplyCharge(cashFee);
            await _transactions.AddAsync(CardTransaction.Charge(card.CardId, TransactionType.Fee,
                $"Cash advance fee - {r.MerchantName.Trim()}", cashFee, now), ct);
            if (cashFeeGst > 0)
            {
                card.ApplyCharge(cashFeeGst);
                await _transactions.AddAsync(CardTransaction.Charge(card.CardId, TransactionType.Tax,
                    $"GST {_billing.Rules.GstPercent:0.##}% on cash advance fee", cashFeeGst, now), ct);
            }
        }

        // 9. ... + cashback (Module 3) + the alert (Module 7). All committed together by ONE SaveChanges:
        //    a swipe can never be saved without its cashback, or cashback without its swipe.
        //    Cash from an ATM is not a purchase, so it earns no cashback (Module 6).
        var cashback = txn.IsCashWithdrawal ? CashbackQuote.None : _cashbackEngine.Calculate(r.Amount, r.MerchantCategoryCode);
        if (cashback.Amount > 0)
        {
            card.CreditReward(cashback.Amount);
            await _cashback.AddAsync(CashbackLog.Earned(txn, cashback.Percentage, cashback.Amount), ct);
        }
        await _notifier.AddAsync(card, Alerts.PurchaseApproved(card, txn, cashback.Amount, cashFee + cashFeeGst), ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return new SwipeResponse(true, nameof(TransactionStatus.Completed), null, txn.TransactionId,
                                 card.MaskedCardNumber, r.Amount, card.AvailableBalance, DateTime.UtcNow,
                                 cashback.Amount, cashback.Percentage);
    }

    /// <summary>Declined swipes are kept in the ledger: useful for the customer and for fraud monitoring.</summary>
    private async Task<SwipeResponse> RecordDeclineAsync(CreditCard card, SwipeRequest r, SwipeOrigin origin, SwipeSource source,
                                                         string reason, CancellationToken ct)
    {
        var txn = CardTransaction.DeclinedSwipe(card.CardId, r.MerchantName, r.MerchantCategoryCode, r.Amount, reason, origin);
        if (source.PartnerSignature is not null) txn.AttachDigitalSignature(source.PartnerSignature);
        await _transactions.AddAsync(txn, ct);
        await _notifier.AddAsync(card, Alerts.PurchaseDeclined(card, txn), ct); // e.g. someone trying your card
        await _unitOfWork.SaveChangesAsync(ct); // also persists the PIN attempt counter
        return Declined(txn.TransactionId, card.MaskedCardNumber, r.Amount, reason);
    }

    private static SwipeResponse Declined(int? transactionId, string? maskedCard, decimal amount, string reason) =>
        new(false, nameof(TransactionStatus.Declined), reason, transactionId, maskedCard, amount, null, DateTime.UtcNow);

    // ---- Load (repayment) --------------------------------------------------------------------

    public async Task<BalanceChangeResponse> LoadAsync(LoadRequest request, CancellationToken ct = default)
    {
        await _loadValidator.ValidateAndThrowAsync(request, ct);
        return await WithConcurrencyRetryAsync(async () =>
        {
            var card = await GetAccessibleCardAsync(request.CardId, ct);

            // Module 4: the part of the bill being repaid through EMI installments cannot be paid here.
            var inEmi = await _emiPlans.GetOutstandingPrincipalAsync(card.CardId, ct);
            var payableNow = Math.Max(0, card.OutstandingAmount - inEmi);
            if (inEmi > 0 && request.Amount > payableNow)
                throw new DomainException(
                    $"You can pay at most {payableNow:0.00} now. {inEmi:0.00} is being repaid through EMI installments.");

            card.Credit(request.Amount);

            var txn = CardTransaction.Load(card.CardId, request.Amount);
            await _transactions.AddAsync(txn, ct);
            await _notifier.AddAsync(card, Alerts.RepaymentReceived(card, request.Amount), ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return new BalanceChangeResponse(ToDto(txn, card), card.ToDto());
        });
    }

    // ---- Refund ------------------------------------------------------------------------------

    public async Task<BalanceChangeResponse> RefundAsync(int transactionId, CancellationToken ct = default)
    {
        EnsureAdmin(); // a refund is initiated by the merchant / bank back office
        return await WithConcurrencyRetryAsync(async () =>
        {
            var original = await _transactions.GetByIdAsync(transactionId, ct)
                           ?? throw new NotFoundException($"Transaction {transactionId} was not found.");
            var card = await _cards.GetByIdAsync(original.CardId, ct)
                       ?? throw new NotFoundException($"Card {original.CardId} was not found.");

            var refund = original.Refund();   // original: Completed -> Refunded
            card.CreditRefund(original.Amount); // give the money back (may create a credit balance)
            await _transactions.AddAsync(refund, ct);

            // Module 3: the purchase no longer exists, so neither does its cashback.
            // The refund was credited first, so the balance always covers the reversal.
            var logs = await _cashback.GetByTransactionIdsAsync(new[] { original.TransactionId }, ct);
            var earned = logs.FirstOrDefault(l => l.CashbackType == CashbackType.Earned);
            if (earned is not null && logs.All(l => l.CashbackType != CashbackType.Reversed))
            {
                card.ReverseReward(earned.CashbackAmount);
                await _cashback.AddAsync(earned.CreateReversal(), ct);
            }
            await _notifier.AddAsync(card, Alerts.RefundCredited(card, refund), ct);

            await _unitOfWork.SaveChangesAsync(ct);

            return new BalanceChangeResponse(ToDto(refund, card), card.ToDto());
        });
    }

    // ---- Queries -------------------------------------------------------------------------------

    public async Task<PagedResult<TransactionDto>> GetCardTransactionsAsync(int cardId, int page, int pageSize, CancellationToken ct = default)
    {
        await GetAccessibleCardAsync(cardId, ct);
        return await QueryAsync(cardId, page, pageSize, ct);
    }

    public Task<PagedResult<TransactionDto>> GetAllTransactionsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        EnsureAdmin();
        return QueryAsync(null, page, pageSize, ct);
    }

    public IReadOnlyList<MerchantCategoryDto> GetMerchantCategories() =>
        MerchantCategoryCodes.Descriptions
            .Where(kv => kv.Key != MerchantCategoryCodes.FinancialInstitution)
            .Select(kv => new MerchantCategoryDto(kv.Key, kv.Value))
            .ToList();

    private async Task<PagedResult<TransactionDto>> QueryAsync(int? cardId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = PagedResult<TransactionDto>.Normalize(page, pageSize);
        var (items, total) = await _transactions.GetPagedAsync(cardId, page, pageSize, ct);

        // Annotate each swipe with its cashback (one extra query for the whole page, not one per row).
        var cashback = (await _cashback.GetByTransactionIdsAsync(items.Select(t => t.TransactionId).ToList(), ct))
                       .ToLookup(c => c.TransactionId);

        var dtos = items.Select(t =>
        {
            var logs = cashback[t.TransactionId].ToList();
            var earned = logs.FirstOrDefault(l => l.CashbackType == CashbackType.Earned);
            return ToDto(t, t.Card) with
            {
                CashbackEarned = earned?.CashbackAmount,
                CashbackReversed = logs.Any(l => l.CashbackType == CashbackType.Reversed)
            };
        }).ToList();

        return new PagedResult<TransactionDto>(dtos, page, pageSize, total);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private Task<T> WithConcurrencyRetryAsync<T>(Func<Task<T>> operation) =>
        _unitOfWork.WithConcurrencyRetryAsync(operation);

    private void EnsureAdmin()
    {
        if (!_currentUser.IsAdmin) throw new ForbiddenException("This operation requires the Admin role.");
    }

    private async Task<CreditCard> GetAccessibleCardAsync(int cardId, CancellationToken ct)
    {
        var card = await _cards.GetByIdAsync(cardId, ct);
        if (card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            throw new NotFoundException($"Card {cardId} was not found.");
        return card;
    }

    private static TransactionDto ToDto(CardTransaction t, CreditCard? card) =>
        new(t.TransactionId, t.CardId, card?.MaskedCardNumber ?? string.Empty, t.MerchantName, t.MerchantCategoryCode,
            t.Amount, t.TransactionType.ToString(), t.TransactionStatus.ToString(), t.DeclineReason,
            t.IsEmiConverted, t.TransactionDate, t.Channel?.ToString(), t.MerchantCountry, t.IsInternational);
}
