using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Emi;

public interface IEmiService
{
    EmiRulesDto GetRules();
    EmiCalculationDto Preview(EmiPreviewRequest request);
    IReadOnlyList<EmiOptionDto> GetOptions(decimal amount);
    Task<IReadOnlyList<EligibleTransactionDto>> GetEligibleTransactionsAsync(int cardId, CancellationToken ct = default);
    Task<EmiPlanDto> ConvertTransactionAsync(int transactionId, ConvertToEmiRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<EmiPlanDto>> GetCardPlansAsync(int cardId, CancellationToken ct = default);
    Task<CardEmiSummaryDto> GetCardSummaryAsync(int cardId, CancellationToken ct = default);
    Task<EmiPlanDto> GetPlanAsync(int emiPlanId, CancellationToken ct = default);
    Task<PayInstallmentResponse> PayInstallmentAsync(int emiPlanId, int installmentNumber, CancellationToken ct = default);
}

/// <summary>
/// EMI conversion engine (spec §1 "EMI Conversion Engine", §4C EmiController).
///
/// Money model:
///  - A purchase already used the credit limit. Converting it to EMI does NOT change AvailableBalance;
///    it only says "this part of what you owe is repaid in fixed monthly installments".
///  - Paying an installment releases its PRINCIPAL part of the limit again. The INTEREST part is the
///    bank's income and does not touch the limit.
///  - "Pay bill" (Load) can no longer pay the amount locked in EMIs (see TransactionService.LoadAsync).
/// </summary>
public class EmiService : IEmiService
{
    private readonly IEmiCalculator _calculator;
    private readonly IEmiPlanRepository _plans;
    private readonly ITransactionRepository _transactions;
    private readonly ICreditCardRepository _cards;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly INotifier _notifier;
    private readonly IValidator<EmiPreviewRequest> _previewValidator;
    private readonly IValidator<ConvertToEmiRequest> _convertValidator;

    public EmiService(IEmiCalculator calculator, IEmiPlanRepository plans, ITransactionRepository transactions,
                      ICreditCardRepository cards, IUnitOfWork unitOfWork, ICurrentUser currentUser, INotifier notifier,
                      IValidator<EmiPreviewRequest> previewValidator, IValidator<ConvertToEmiRequest> convertValidator)
    {
        _calculator = calculator;
        _plans = plans;
        _transactions = transactions;
        _cards = cards;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _notifier = notifier;
        _previewValidator = previewValidator;
        _convertValidator = convertValidator;
    }

    // ---- Calculator ---------------------------------------------------------------------

    public EmiRulesDto GetRules() =>
        new(_calculator.Rules.MinimumAmount, _calculator.Rules.ConversionWindowDays,
            _calculator.AvailableTenures.Select(t => new EmiOptionRateDto(t, _calculator.GetAnnualRate(t))).ToList());

    public EmiCalculationDto Preview(EmiPreviewRequest request)
    {
        _previewValidator.ValidateAndThrow(request);
        var calc = _calculator.Calculate(request.PrincipalAmount, request.TenureMonths, Today);
        return new EmiCalculationDto(calc.PrincipalAmount, calc.TenureMonths, calc.AnnualInterestRate,
            calc.MonthlyInstallment, calc.TotalInterest, calc.TotalRepayable,
            calc.Schedule.Select(l => new EmiScheduleItemDto(l.InstallmentNumber, l.DueDate, l.Amount, l.Principal,
                                                             l.Interest, nameof(InstallmentStatus.Pending), null, false)).ToList());
    }

    public IReadOnlyList<EmiOptionDto> GetOptions(decimal amount)
    {
        if (amount <= 0 || amount > Transactions.TransactionLimits.MaxAmount)
            throw new DomainException("Amount must be greater than zero.");
        return _calculator.AvailableTenures.Select(t =>
        {
            var c = _calculator.Calculate(amount, t, Today);
            return new EmiOptionDto(t, c.AnnualInterestRate, c.MonthlyInstallment, c.TotalInterest, c.TotalRepayable);
        }).ToList();
    }

    // ---- Conversion ---------------------------------------------------------------------

    public async Task<IReadOnlyList<EligibleTransactionDto>> GetEligibleTransactionsAsync(int cardId, CancellationToken ct = default)
    {
        var card = await GetAccessibleCardAsync(cardId, ct);
        if (!card.IsActive) return Array.Empty<EligibleTransactionDto>();

        var now = DateTime.UtcNow;
        var window = _calculator.Rules.ConversionWindowDays;
        var swipes = await _transactions.GetConvertibleSwipesAsync(cardId, now.AddDays(-window), ct);

        return swipes
            .Where(t => t.GetEmiIneligibilityReason(_calculator.Rules.MinimumAmount, window, now) is null)
            .Select(t => new EligibleTransactionDto(t.TransactionId, t.CardId, t.MerchantName, t.Amount,
                                                    t.TransactionDate, t.TransactionDate.AddDays(window)))
            .ToList();
    }

    public async Task<EmiPlanDto> ConvertTransactionAsync(int transactionId, ConvertToEmiRequest request, CancellationToken ct = default)
    {
        await _convertValidator.ValidateAndThrowAsync(request, ct);

        return await _unitOfWork.WithConcurrencyRetryAsync(async () =>
        {
            // 1. Fetch the purchase (spec step 1) and check the caller may use the card.
            var purchase = await _transactions.GetByIdAsync(transactionId, ct)
                           ?? throw new NotFoundException($"Transaction {transactionId} was not found.");
            var card = await GetAccessibleCardAsync(purchase.CardId, ct);
            if (!card.IsActive) throw new DomainException("EMI conversion is not available on a blocked card.");

            // 2. Validate eligibility (spec step 2) and mark the purchase. Transactions.IsEmiConverted and
            //    TransactionStatus are concurrency tokens, so a refund running at the same moment cannot
            //    also succeed - one of the two saves fails and is retried against fresh data.
            var now = DateTime.UtcNow;
            purchase.MarkEmiConverted(_calculator.Rules.MinimumAmount, _calculator.Rules.ConversionWindowDays, now);

            // 3. Compute the plan and schedule (spec step 3).
            var calc = _calculator.Calculate(purchase.Amount, request.TenureMonths, DateOnly.FromDateTime(now));
            var plan = EmiPlan.Create(purchase, calc.TenureMonths, calc.AnnualInterestRate, calc.MonthlyInstallment, calc.Schedule);

            // 4. Save plan + schedules + the converted flag in one database transaction (spec step 4).
            await _plans.AddAsync(plan, ct);
            await _notifier.AddAsync(card, Alerts.ConvertedToEmi(card, plan, purchase), ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return ToDto(plan, purchase);
        });
    }

    // ---- Plans ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<EmiPlanDto>> GetCardPlansAsync(int cardId, CancellationToken ct = default)
    {
        await GetAccessibleCardAsync(cardId, ct);
        return (await _plans.GetByCardAsync(cardId, ct)).Select(p => ToDto(p, p.Transaction)).ToList();
    }

    public async Task<CardEmiSummaryDto> GetCardSummaryAsync(int cardId, CancellationToken ct = default)
    {
        var card = await GetAccessibleCardAsync(cardId, ct);
        var active = (await _plans.GetByCardAsync(cardId, ct)).Where(p => p.PlanStatus == EmiPlanStatus.Active).ToList();
        var next = active.Select(p => p.NextInstallment).Where(s => s is not null).MinBy(s => s!.DueDate);
        var principal = active.Sum(p => p.OutstandingPrincipal);

        return new CardEmiSummaryDto(cardId, active.Count, principal, active.Sum(p => p.RemainingBalance),
            Math.Max(0, card.OutstandingAmount - principal), next?.DueDate, next?.AmountDue);
    }

    public async Task<EmiPlanDto> GetPlanAsync(int emiPlanId, CancellationToken ct = default)
    {
        var plan = await GetAccessiblePlanAsync(emiPlanId, ct);
        return ToDto(plan, plan.Transaction);
    }

    public async Task<PayInstallmentResponse> PayInstallmentAsync(int emiPlanId, int installmentNumber, CancellationToken ct = default)
    {
        return await _unitOfWork.WithConcurrencyRetryAsync(async () =>
        {
            var plan = await GetAccessiblePlanAsync(emiPlanId, ct);
            var card = await _cards.GetByIdAsync(plan.CardId, ct)
                       ?? throw new NotFoundException($"Card {plan.CardId} was not found.");

            var installment = plan.Schedules.SingleOrDefault(s => s.InstallmentNumber == installmentNumber)
                              ?? throw new DomainException($"Installment {installmentNumber} does not exist.");

            var payment = CardTransaction.EmiInstallment(card.CardId,
                $"EMI {installmentNumber}/{plan.TenureMonths} - {plan.Transaction?.MerchantName}", installment.AmountDue);
            await _transactions.AddAsync(payment, ct);

            plan.PayInstallment(installmentNumber, payment, DateTime.UtcNow); // order + "already paid" rules
            card.ReleaseEmiPrincipal(installment.PrincipalComponent);          // principal part frees the limit
            await _notifier.AddAsync(card, Alerts.EmiInstallmentPaid(card, plan, installmentNumber, installment.AmountDue), ct);

            // Plan, schedule row, ledger row and card balance: one database transaction.
            // A double-clicked "Pay" fails the card's concurrency check, is retried, and then stops at
            // "Installment N is already paid" - it can never pay the next installment by accident.
            await _unitOfWork.SaveChangesAsync(ct);

            return new PayInstallmentResponse(ToDto(plan, plan.Transaction), card.ToDto(), payment.TransactionId);
        });
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<CreditCard> GetAccessibleCardAsync(int cardId, CancellationToken ct)
    {
        var card = await _cards.GetByIdAsync(cardId, ct);
        if (card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            throw new NotFoundException($"Card {cardId} was not found.");
        return card;
    }

    private async Task<EmiPlan> GetAccessiblePlanAsync(int emiPlanId, CancellationToken ct)
    {
        var plan = await _plans.GetByIdAsync(emiPlanId, ct)
                   ?? throw new NotFoundException($"EMI plan {emiPlanId} was not found.");
        await GetAccessibleCardAsync(plan.CardId, ct); // same 404 for someone else's plan
        return plan;
    }

    private static EmiPlanDto ToDto(EmiPlan p, CardTransaction? purchase)
    {
        var today = Today;
        var schedule = p.Schedules.OrderBy(s => s.InstallmentNumber).Select(s => ToDto(s, today)).ToList();
        var next = p.NextInstallment;
        return new EmiPlanDto(p.EmiPlanId, p.TransactionId, p.CardId, purchase?.MerchantName ?? string.Empty,
            purchase?.TransactionDate ?? p.CreatedDate, p.PrincipalAmount, p.TenureMonths, p.AnnualInterestRate,
            p.MonthlyInstallment, p.TotalInterest, p.TotalRepayable, p.RemainingBalance, p.OutstandingPrincipal,
            p.PaidInstallments, p.PlanStatus.ToString(), p.CreatedDate,
            next is null ? null : ToDto(next, today), schedule);
    }

    private static EmiScheduleItemDto ToDto(EmiSchedule s, DateOnly today) =>
        new(s.InstallmentNumber, s.DueDate, s.AmountDue, s.PrincipalComponent, s.InterestComponent,
            s.PaymentStatus.ToString(), s.PaidDate, s.IsOverdue(today));
}
