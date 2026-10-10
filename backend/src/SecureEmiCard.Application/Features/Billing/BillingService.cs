using SecureEmiCard.Application.Abstractions.Billing;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Billing;

/// <summary>Billing for people: statements, PDF, "your bill". Every call checks who may see the card.</summary>
public interface IBillingService
{
    BillingRulesDto GetRules();

    /// <summary>Back office: close the card's billing cycle now (the scheduler does it every cycle).</summary>
    Task<StatementDto> GenerateStatementAsync(int cardId, CancellationToken ct = default);

    Task<IReadOnlyList<StatementDto>> GetStatementsAsync(int cardId, CancellationToken ct = default);
    Task<StatementDetailDto> GetStatementAsync(int statementId, CancellationToken ct = default);
    Task<StatementPdf> GetStatementPdfAsync(int statementId, CancellationToken ct = default);
    Task<CardBillingSummaryDto> GetSummaryAsync(int cardId, CancellationToken ct = default);
}

/// <summary>Billing for the scheduler (no signed-in user): one small step per call, so one bad card can't stop the run.</summary>
public interface IBillingCycleRunner
{
    Task<IReadOnlyList<int>> GetStatementsToAssessAsync(CancellationToken ct = default);
    Task AssessStatementAsync(int statementId, CancellationToken ct = default);
    Task<int> SendPaymentRemindersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetCardsDueForStatementAsync(CancellationToken ct = default);
    Task GenerateScheduledStatementAsync(int cardId, CancellationToken ct = default);
}

/// <summary>
/// Billing cycle (Module 8).
///
/// Generating a statement:
///   1. decide the outcome of earlier statements whose due date passed (late fee, interest),
///   2. interest on cash withdrawals (no interest-free period for cash),
///   3. take everything NOT BILLED YET (ledger rows, cashback, moves to EMI), add it up, mark it billed,
///   4. total due, minimum due, due date, an alert - all in ONE database transaction.
/// "Unbilled → billed" instead of "dated between A and B": a purchase saved a millisecond after the statement
/// simply goes on the next one; nothing can fall between two statements, whatever the timing.
/// </summary>
public class BillingService : IBillingService, IBillingCycleRunner
{
    private readonly ICreditCardRepository _cards;
    private readonly ICardStatementRepository _statements;
    private readonly ITransactionRepository _transactions;
    private readonly ICashbackRepository _cashback;
    private readonly IEmiPlanRepository _emiPlans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly INotifier _notifier;
    private readonly BillingCalculator _calculator;
    private readonly IStatementPdfRenderer _pdf;
    private readonly ICardControlRules _bankTime;
    private readonly TimeProvider _clock;

    public BillingService(ICreditCardRepository cards, ICardStatementRepository statements, ITransactionRepository transactions,
                          ICashbackRepository cashback, IEmiPlanRepository emiPlans, IUnitOfWork unitOfWork,
                          ICurrentUser currentUser, INotifier notifier, BillingCalculator calculator,
                          IStatementPdfRenderer pdf, ICardControlRules bankTime, TimeProvider clock)
    {
        _cards = cards;
        _statements = statements;
        _transactions = transactions;
        _cashback = cashback;
        _emiPlans = emiPlans;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _notifier = notifier;
        _calculator = calculator;
        _pdf = pdf;
        _bankTime = bankTime;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private BillingOptions Rules => _calculator.Rules;

    // ---- people ---------------------------------------------------------------------------------

    public BillingRulesDto GetRules() =>
        new(Rules.MinimumDuePercent, Rules.MinimumDueFloor, Rules.MonthlyInterestPercent, Rules.CashAdvanceFeePercent,
            Rules.CashAdvanceMinimumFee, Rules.GstPercent, (int)Math.Round(Rules.PaymentDuePeriod.TotalDays),
            _calculator.LateFeeSlabs.Select(s => new LateFeeSlabDto(s.UpTo, s.Fee)).ToList());

    public async Task<StatementDto> GenerateStatementAsync(int cardId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAdmin) throw new ForbiddenException("Only the bank can close a billing cycle.");
        return ToDto((await GenerateCoreAsync(cardId, scheduled: false, ct))!);
    }

    public async Task<IReadOnlyList<StatementDto>> GetStatementsAsync(int cardId, CancellationToken ct = default)
    {
        await GetAccessibleCardAsync(cardId, ct);
        return (await _statements.GetByCardAsync(cardId, ct)).Select(ToDto).ToList();
    }

    public async Task<StatementDetailDto> GetStatementAsync(int statementId, CancellationToken ct = default)
    {
        var statement = await _statements.GetByIdAsync(statementId, ct);
        var card = statement?.Card;
        if (statement is null || card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            throw new NotFoundException($"Statement {statementId} was not found.");

        var lines = new List<StatementLineDto>();
        foreach (var t in await _transactions.GetByStatementAsync(statementId, ct))
            lines.Add(ToLine(t));
        // A reversal is stored with a negative amount, so "−amount" is right for both kinds.
        foreach (var c in await _cashback.GetByStatementAsync(statementId, ct))
            lines.Add(new StatementLineDto(c.CreditedDate,
                $"{(c.CashbackType == CashbackType.Earned ? "Cashback" : "Cashback reversed")} - {c.Transaction?.MerchantName}",
                "Cashback", -c.CashbackAmount));
        foreach (var p in await _emiPlans.GetByStatementAsync(statementId, ct))
            lines.Add(new StatementLineDto(p.CreatedDate,
                $"Moved to EMI - {p.Transaction?.MerchantName} ({p.TenureMonths} months)", "Emi", -p.PrincipalAmount));

        return new StatementDetailDto(ToDto(statement), card.Cardholder?.FullName ?? string.Empty, card.MaskedCardNumber,
            card.CreditLimit, lines.OrderBy(l => l.Date).ToList(), GetRules(), _bankTime.Options.BusinessDayUtcOffset);
    }

    public async Task<StatementPdf> GetStatementPdfAsync(int statementId, CancellationToken ct = default)
    {
        var detail = await GetStatementAsync(statementId, ct);
        var date = (detail.Statement.PeriodEnd + detail.UtcOffset).ToString("yyyy-MM-dd");
        return new StatementPdf($"statement-{detail.MaskedCardNumber[^4..]}-{date}.pdf", _pdf.Render(detail));
    }

    public async Task<CardBillingSummaryDto> GetSummaryAsync(int cardId, CancellationToken ct = default)
    {
        var card = await GetAccessibleCardAsync(cardId, ct);
        var now = Now;
        var last = await _statements.GetLatestAsync(cardId, ct);

        decimal paidSince = 0, paidByDue = 0;
        if (last is not null)
        {
            paidSince = await _transactions.SumPaymentsAsync(cardId, last.PeriodEnd, now, ct);
            paidByDue = await _transactions.SumPaymentsAsync(cardId, last.PeriodEnd, last.DueDate, ct);
        }

        var unbilled = Unbilled(await _transactions.GetUnbilledAsync(cardId, ct),
                                await _cashback.GetUnbilledAsync(cardId, ct), await _emiPlans.GetUnbilledAsync(cardId, ct));
        var isOverdue = last is not null && (last.Status == StatementStatus.Overdue ||
                                             (!last.IsAssessed && now > last.DueDate && paidByDue < last.MinimumDue));

        return new CardBillingSummaryDto(cardId, last is null ? null : ToDto(last), paidSince,
            last is null ? 0 : Math.Max(0, last.MinimumDue - paidSince),
            last is null ? 0 : Math.Max(0, last.ClosingBalance - paidSince),
            isOverdue, unbilled, (card.LastStatementDate ?? card.CreatedAt) + Rules.CycleLength);
    }

    // ---- scheduler --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<int>> GetStatementsToAssessAsync(CancellationToken ct = default) =>
        (await _statements.GetUnassessedPastDueAsync(null, Now, ct)).Select(s => s.StatementId).ToList();

    public Task AssessStatementAsync(int statementId, CancellationToken ct = default) =>
        _unitOfWork.WithConcurrencyRetryAsync(async () =>
        {
            var statement = await _statements.GetByIdAsync(statementId, ct)
                            ?? throw new NotFoundException($"Statement {statementId} was not found.");
            var now = Now;
            if (statement.IsAssessed || now <= statement.DueDate) return true;
            var card = await _cards.GetByIdAsync(statement.CardId, ct)
                       ?? throw new NotFoundException($"Card {statement.CardId} was not found.");

            await AssessAsync(card, statement, now, new List<CardTransaction>(), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        });

    public async Task<int> SendPaymentRemindersAsync(CancellationToken ct = default)
    {
        var now = Now;
        var sent = 0;
        foreach (var statement in await _statements.GetReminderCandidatesAsync(now, now + Rules.ReminderBeforeDue, ct))
        {
            var paid = await _transactions.SumPaymentsAsync(statement.CardId, statement.PeriodEnd, now, ct);
            var card = await _cards.GetByIdAsync(statement.CardId, ct);
            if (card is not null && paid < statement.MinimumDue)
            {
                await _notifier.AddAsync(card, Alerts.PaymentReminder(card, statement, statement.MinimumDue - paid, BankDate), ct);
                sent++;
            }
            statement.MarkReminderSent(now); // also when already paid: one decision per statement
        }
        await _unitOfWork.SaveChangesAsync(ct);
        return sent;
    }

    public Task<IReadOnlyList<int>> GetCardsDueForStatementAsync(CancellationToken ct = default) =>
        _cards.GetIdsWithCycleStartedOnOrBeforeAsync(Now - Rules.CycleLength, ct);

    public Task GenerateScheduledStatementAsync(int cardId, CancellationToken ct = default) =>
        GenerateCoreAsync(cardId, scheduled: true, ct);

    // ---- the billing run --------------------------------------------------------------------------

    /// <returns>The new statement, or null when the scheduler finds the cycle was closed a moment ago.</returns>
    private Task<CardStatement?> GenerateCoreAsync(int cardId, bool scheduled, CancellationToken ct) =>
        _unitOfWork.WithConcurrencyRetryAsync<CardStatement?>(async () =>
        {
            var card = await _cards.GetByIdAsync(cardId, ct) ?? throw new NotFoundException($"Card {cardId} was not found.");
            var now = Now;
            // Checked again inside the retry: if the bank closed this cycle in the meantime, there is nothing to do.
            if (scheduled && (card.LastStatementDate ?? card.CreatedAt) + Rules.CycleLength > now) return null;
            var previous = await _statements.GetLatestAsync(cardId, ct);
            if (previous is not null && previous.PeriodEnd >= now)
                throw new ConflictException("A statement for this card was generated a moment ago.");

            // 1. Earlier statements whose due date has passed (normally the scheduler did this already).
            var newCharges = new List<CardTransaction>();
            foreach (var due in await _statements.GetUnassessedPastDueAsync(cardId, now, ct))
                await AssessAsync(card, due, now, newCharges, ct);

            // 2. Everything not on a statement yet.
            var rows = await _transactions.GetUnbilledAsync(cardId, ct);
            var cashback = await _cashback.GetUnbilledAsync(cardId, ct);
            var movedToEmi = await _emiPlans.GetUnbilledAsync(cardId, ct);

            // 3. Cash has no interest-free period: interest from the day of each withdrawal until today.
            var cashInterest = rows.Where(t => t.TransactionType == TransactionType.Swipe && t.IsCashWithdrawal)
                                   .Sum(t => _calculator.CashInterest(t.Amount, BankDay(t.TransactionDate), BankDay(now)));
            if (cashInterest > 0)
                await PostChargeWithGstAsync(card, TransactionType.Interest, "Interest on cash withdrawals", cashInterest, now, newCharges, ct);

            // 4. Add it up. Opening + movements = closing, by construction.
            var billed = rows.Concat(newCharges).ToList();
            var figures = Figures(previous?.ClosingBalance ?? 0m, billed, cashback, movedToEmi);
            var pastDue = previous is { Status: StatementStatus.Overdue, PaidByDueDate: { } paid }
                ? Math.Max(0, previous.MinimumDue - paid) : 0m;
            var dueDate = now + Rules.PaymentDuePeriod;

            var statement = CardStatement.Create(cardId, previous?.PeriodEnd ?? card.CreatedAt, now, dueDate, figures,
                _calculator.MinimumDue(figures.ClosingBalance, figures.FeesAndCharges, pastDue),
                await _emiPlans.GetInstallmentsDueByAsync(cardId, DateOnly.FromDateTime(dueDate), ct));

            foreach (var t in billed) t.MarkBilled(statement);
            foreach (var c in cashback) c.MarkBilled(statement);
            foreach (var p in movedToEmi) p.MarkBilled(statement);
            card.MarkStatementGenerated(now);

            await _statements.AddAsync(statement, ct);
            await _notifier.AddAsync(card, Alerts.StatementReady(card, statement, BankDate), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return statement;
        });

    /// <summary>The due date passed: what was paid in time decides late fee and interest.</summary>
    private async Task AssessAsync(CreditCard card, CardStatement statement, DateTime now,
                                   List<CardTransaction> newCharges, CancellationToken ct)
    {
        var paid = await _transactions.SumPaymentsAsync(card.CardId, statement.PeriodEnd, statement.DueDate, ct);
        var outcome = statement.Assess(paid, now);
        if (outcome.Status == StatementStatus.Paid) return;

        var label = BankDate(statement.PeriodEnd);
        var lateFee = outcome.Status == StatementStatus.Overdue ? _calculator.LateFee(outcome.UnpaidBalance) : 0m;
        if (lateFee > 0)
            await PostChargeWithGstAsync(card, TransactionType.Fee, $"Late payment fee - statement {label}", lateFee, now, newCharges, ct);

        var interest = _calculator.Interest(statement.ClosingBalance, statement.FeesAndCharges, paid);
        if (interest > 0)
            await PostChargeWithGstAsync(card, TransactionType.Interest, $"Interest - statement {label}", interest, now, newCharges, ct);

        if (outcome.Status == StatementStatus.Overdue || interest > 0)
            await _notifier.AddAsync(card, Alerts.StatementOutcome(card, statement, outcome, lateFee, interest,
                _calculator.Gst(lateFee) + _calculator.Gst(interest), BankDate), ct);
    }

    private async Task PostChargeWithGstAsync(CreditCard card, TransactionType type, string description, decimal amount,
                                              DateTime now, List<CardTransaction> newCharges, CancellationToken ct)
    {
        await PostChargeAsync(card, type, description, amount, now, newCharges, ct);
        var gst = _calculator.Gst(amount);
        if (gst > 0)
            await PostChargeAsync(card, TransactionType.Tax, $"GST {Rules.GstPercent:0.##}% on {description}", gst, now, newCharges, ct);
    }

    private async Task PostChargeAsync(CreditCard card, TransactionType type, string description, decimal amount,
                                       DateTime now, List<CardTransaction> newCharges, CancellationToken ct)
    {
        var charge = CardTransaction.Charge(card.CardId, type, description, amount, now);
        card.ApplyCharge(amount);
        await _transactions.AddAsync(charge, ct);
        newCharges.Add(charge);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static StatementFigures Figures(decimal opening, IReadOnlyCollection<CardTransaction> rows,
                                            IEnumerable<CashbackLog> cashback, IEnumerable<EmiPlan> movedToEmi)
    {
        decimal Sum(Func<CardTransaction, bool> which) => rows.Where(which).Sum(t => t.Amount);

        var purchases = Sum(t => t.TransactionType == TransactionType.Swipe && !t.IsCashWithdrawal);
        var cash = Sum(t => t.TransactionType == TransactionType.Swipe && t.IsCashWithdrawal);
        var charges = Sum(t => t.IsCharge);
        var payments = Sum(t => t.TransactionType == TransactionType.Load);
        var refunds = Sum(t => t.TransactionType == TransactionType.Refund);
        var cashbackNet = cashback.Sum(c => c.CashbackAmount); // reversals are stored negative
        var emi = movedToEmi.Sum(p => p.PrincipalAmount);

        var closing = opening + purchases + cash + charges - payments - refunds - cashbackNet - emi;
        return new StatementFigures(opening, purchases, cash, charges, payments, refunds, cashbackNet, emi, closing);
    }

    /// <summary>Signed total of everything not on a statement yet ("since your last statement").</summary>
    private static decimal Unbilled(IReadOnlyCollection<CardTransaction> rows, IEnumerable<CashbackLog> cashback, IEnumerable<EmiPlan> plans)
    {
        var f = Figures(0m, rows, cashback, plans);
        return f.ClosingBalance;
    }

    private static StatementLineDto ToLine(CardTransaction t) => t.TransactionType switch
    {
        TransactionType.Swipe when t.IsCashWithdrawal => new(t.TransactionDate, $"Cash withdrawal - {t.MerchantName}", "Cash", t.Amount),
        TransactionType.Swipe => new(t.TransactionDate,
            t.TransactionStatus == TransactionStatus.Refunded ? $"{t.MerchantName} (refunded later)" : t.MerchantName,
            "Purchase", t.Amount),
        TransactionType.Load => new(t.TransactionDate, "Payment received - thank you", "Payment", -t.Amount),
        TransactionType.Refund => new(t.TransactionDate, $"Refund - {t.MerchantName}", "Refund", -t.Amount),
        TransactionType.Fee => new(t.TransactionDate, t.MerchantName, "Fee", t.Amount),
        TransactionType.Interest => new(t.TransactionDate, t.MerchantName, "Interest", t.Amount),
        TransactionType.Tax => new(t.TransactionDate, t.MerchantName, "Tax", t.Amount),
        _ => new(t.TransactionDate, t.MerchantName, t.TransactionType.ToString(), t.Amount)
    };

    private DateOnly BankDay(DateTime utc) => DateOnly.FromDateTime(utc + _bankTime.Options.BusinessDayUtcOffset);

    /// <summary>A date as the customer reads it: in the bank's time zone (IST by default).</summary>
    private string BankDate(DateTime utc) =>
        (utc + _bankTime.Options.BusinessDayUtcOffset).ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<CreditCard> GetAccessibleCardAsync(int cardId, CancellationToken ct)
    {
        var card = await _cards.GetByIdAsync(cardId, ct);
        if (card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            throw new NotFoundException($"Card {cardId} was not found.");
        return card;
    }

    private static StatementDto ToDto(CardStatement s) =>
        new(s.StatementId, s.CardId, s.PeriodStart, s.PeriodEnd, s.DueDate, s.OpeningBalance, s.Purchases, s.CashWithdrawals,
            s.FeesAndCharges, s.Payments, s.Refunds, s.Cashback, s.MovedToEmi, s.ClosingBalance, s.MinimumDue,
            s.EmiInstallmentsDue, s.Status.ToString(), s.PaidByDueDate);
}
