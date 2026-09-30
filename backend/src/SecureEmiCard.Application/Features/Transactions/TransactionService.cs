using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Transactions;

public interface ITransactionService
{
    Task<SwipeResponse> SwipeAsync(SwipeRequest request, CancellationToken ct = default);
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
    private const int MaxConcurrencyRetries = 3;

    private readonly ICreditCardRepository _cards;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICardLookupHasher _lookupHasher;
    private readonly ISecretHasher _secretHasher;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<SwipeRequest> _swipeValidator;
    private readonly IValidator<LoadRequest> _loadValidator;

    public TransactionService(ICreditCardRepository cards, ITransactionRepository transactions, IUnitOfWork unitOfWork,
                              ICardLookupHasher lookupHasher, ISecretHasher secretHasher, ICurrentUser currentUser,
                              IValidator<SwipeRequest> swipeValidator, IValidator<LoadRequest> loadValidator)
    {
        _cards = cards;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _lookupHasher = lookupHasher;
        _secretHasher = secretHasher;
        _currentUser = currentUser;
        _swipeValidator = swipeValidator;
        _loadValidator = loadValidator;
    }

    // ---- Swipe (authorization) ------------------------------------------------------------

    public async Task<SwipeResponse> SwipeAsync(SwipeRequest request, CancellationToken ct = default)
    {
        await _swipeValidator.ValidateAndThrowAsync(request, ct);
        return await WithConcurrencyRetryAsync(() => AuthorizeAsync(request, ct));
    }

    private async Task<SwipeResponse> AuthorizeAsync(SwipeRequest r, CancellationToken ct)
    {
        // 1. Find the card through the blind index - the number itself is never searched or logged.
        var card = await _cards.GetByNumberHashAsync(_lookupHasher.Compute(r.CardNumber), ct);

        // Cardholders use the simulator with their own cards only; admins act as the merchant terminal.
        // An unknown or foreign card cannot be linked to a ledger row, so nothing is recorded.
        if (card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            return Declined(null, null, r.Amount, DeclineReasons.InvalidCardDetails);

        // 2. Card-not-present data: expiry date and CVV. Same generic reason for both on purpose.
        bool expiryMatches = card.ExpiryDate.Month == r.ExpiryMonth && card.ExpiryDate.Year == r.ExpiryYear;
        if (!expiryMatches || !_secretHasher.Verify(r.Cvv, card.CvvHash))
            return await RecordDeclineAsync(card, r, DeclineReasons.InvalidCardDetails, ct);

        // 3. Card state. Checked before the PIN so a blocked card cannot be used to guess PINs.
        if (!card.IsActive) return await RecordDeclineAsync(card, r, DeclineReasons.CardBlocked, ct);
        if (card.IsExpired) return await RecordDeclineAsync(card, r, DeclineReasons.CardExpired, ct);

        // 4. PIN with 3-strikes lockout.
        var pin = PinCheck.Verify(_secretHasher, card, r.Pin);
        if (pin == PinCheckResult.LockedOut) return await RecordDeclineAsync(card, r, DeclineReasons.PinTriesExceeded, ct);
        if (pin == PinCheckResult.Invalid) return await RecordDeclineAsync(card, r, DeclineReasons.IncorrectPin, ct);

        // 5. Funds.
        var reason = card.GetSwipeDeclineReason(r.Amount);
        if (reason is not null) return await RecordDeclineAsync(card, r, reason, ct);

        // 6. Approve: debit + ledger row, committed together.
        card.Debit(r.Amount);
        var txn = CardTransaction.ApprovedSwipe(card.CardId, r.MerchantName, r.MerchantCategoryCode, r.Amount);
        await _transactions.AddAsync(txn, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new SwipeResponse(true, nameof(TransactionStatus.Completed), null, txn.TransactionId,
                                 card.MaskedCardNumber, r.Amount, card.AvailableBalance, DateTime.UtcNow);
    }

    /// <summary>Declined swipes are kept in the ledger: useful for the customer and for fraud monitoring.</summary>
    private async Task<SwipeResponse> RecordDeclineAsync(CreditCard card, SwipeRequest r, string reason, CancellationToken ct)
    {
        var txn = CardTransaction.DeclinedSwipe(card.CardId, r.MerchantName, r.MerchantCategoryCode, r.Amount, reason);
        await _transactions.AddAsync(txn, ct);
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
            card.Credit(request.Amount);

            var txn = CardTransaction.Load(card.CardId, request.Amount);
            await _transactions.AddAsync(txn, ct);
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
        return new PagedResult<TransactionDto>(items.Select(t => ToDto(t, t.Card)).ToList(), page, pageSize, total);
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Runs an operation and, if another request changed the same card in the meantime,
    /// discards the stale data and runs it again (up to 3 times).
    /// </summary>
    private async Task<T> WithConcurrencyRetryAsync<T>(Func<Task<T>> operation)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException) when (attempt < MaxConcurrencyRetries)
            {
                _unitOfWork.ClearChanges();
            }
        }
    }

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
            t.IsEmiConverted, t.TransactionDate);
}
