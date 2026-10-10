using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 8: full billing cycles against EF Core in-memory, with a clock the test can move forward.</summary>
public class BillingFlowTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private static readonly TimeSpan AfterDueDate = TimeSpan.FromDays(21);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly TestServices _services;
    private readonly CardService _cards;
    private readonly TransactionService _transactions;
    private readonly EmiService _emi;
    private readonly BillingService _billing;
    private readonly int _aliceId;
    private readonly int _bobId;

    public BillingFlowTests()
    {
        _db = NewContext();
        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        var bob = new Cardholder("Bob", "B", "bob@test.com", "+911234568", "hash");
        _db.Cardholders.AddRange(alice, bob);
        _db.SaveChanges();
        _aliceId = alice.CardholderId;
        _bobId = bob.CardholderId;

        _services = new TestServices(_db, _user);
        _cards = _services.Cards();
        _transactions = _services.Transactions();
        _emi = _services.Emi();
        _billing = _services.BillingService();
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task<IssuedCardResponse> IssueToAlice(decimal limit = 100_000m)
    {
        _user.IsAdmin = true;
        var issued = await _cards.IssueCardAsync(new IssueCardRequest(_aliceId, limit));
        AsAlice();
        return issued;
    }

    private void AsAlice()
    {
        _user.IsAdmin = false;
        _user.UserId = _aliceId;
    }

    private async Task<T> AsAdmin<T>(Func<Task<T>> action)
    {
        _user.IsAdmin = true;
        try { return await action(); }
        finally { AsAlice(); }
    }

    private Task<SwipeResponse> Buy(IssuedCardResponse c, decimal amount, string mcc = MerchantCategoryCodes.Electronics,
                                    TransactionChannel channel = TransactionChannel.Pos) =>
        _transactions.SwipeAsync(new SwipeRequest(c.CardNumber, c.Card.ExpiryDate.Month, c.Card.ExpiryDate.Year, c.Cvv,
            c.InitialPin, channel == TransactionChannel.Atm ? "SBI ATM" : "Croma", mcc, amount, channel));

    private Task<SwipeResponse> WithdrawCash(IssuedCardResponse c, decimal amount) =>
        Buy(c, amount, MerchantCategoryCodes.CashWithdrawal, TransactionChannel.Atm);

    private Task<StatementDto> Generate(IssuedCardResponse c) => AsAdmin(() => _billing.GenerateStatementAsync(c.Card.CardId));

    private Task Pay(IssuedCardResponse c, decimal amount) => _transactions.LoadAsync(new LoadRequest(c.Card.CardId, amount));

    /// <summary>What the customer owes outside EMIs, from the card itself - every statement must close at this.</summary>
    private async Task<decimal> BillableBalanceAsync(int cardId)
    {
        var card = await _db.CreditCards.AsNoTracking().SingleAsync(c => c.CardId == cardId);
        var emi = await new SecureEmiCard.Infrastructure.Persistence.Repositories.EmiPlanRepository(_db).GetOutstandingPrincipalAsync(cardId);
        return card.OutstandingAmount - emi;
    }

    // ---- tests --------------------------------------------------------------------------------------

    [Fact]
    public async Task First_statement_adds_up_every_kind_of_movement()
    {
        var card = await IssueToAlice();
        await Buy(card, 10_000m);                                                       // cashback 100
        var groceries = await Buy(card, 2_000m, MerchantCategoryCodes.Groceries);       // cashback 60
        await AsAdmin(() => _transactions.RefundAsync(groceries.TransactionId!.Value));  // refund 2,000, cashback −60
        var tv = await Buy(card, 12_000m);                                              // cashback 120
        await _emi.ConvertTransactionAsync(tv.TransactionId!.Value, new ConvertToEmiRequest(12));
        await Pay(card, 1_000m);
        await WithdrawCash(card, 3_000m);                                               // fee 500 + GST 90

        var s = await Generate(card);

        Assert.Equal(24_000m, s.Purchases);
        Assert.Equal(3_000m, s.CashWithdrawals);
        Assert.Equal(594.13m, s.FeesAndCharges);         // 500 + 90 + cash interest 1 day 3.50 + GST 0.63
        Assert.Equal(1_000m, s.Payments);
        Assert.Equal(2_000m, s.Refunds);
        Assert.Equal(220m, s.Cashback);
        Assert.Equal(12_000m, s.MovedToEmi);
        Assert.Equal(12_374.13m, s.ClosingBalance);
        Assert.Equal(await BillableBalanceAsync(card.Card.CardId), s.ClosingBalance);
        Assert.Equal(1_183.13m, s.MinimumDue);          // 5 % of 11,780 + 594.13 charges

        var detail = await _billing.GetStatementAsync(s.StatementId);
        Assert.Equal(s.ClosingBalance - s.OpeningBalance, detail.Lines.Sum(l => l.Amount));
        Assert.Contains(detail.Lines, l => l.Kind == "Emi" && l.Amount == -12_000m);
        Assert.Contains(await _db.Notifications.Select(n => n.Title).ToListAsync(), t => t.StartsWith("Statement ready"));
    }

    [Fact]
    public async Task Paid_in_full_by_the_due_date_means_no_interest_and_no_fee()
    {
        var card = await IssueToAlice();
        await Buy(card, 5_000m);
        var s1 = await Generate(card);
        await Pay(card, s1.ClosingBalance);

        _services.Clock.Advance(AfterDueDate);
        var s2 = await Generate(card);

        Assert.Equal(nameof(StatementStatus.Paid), (await _billing.GetStatementAsync(s1.StatementId)).Statement.Status);
        Assert.Equal(0m, s2.FeesAndCharges);
        Assert.Equal(0m, s2.ClosingBalance);
        Assert.Equal(nameof(StatementStatus.Paid), s2.Status);
    }

    [Fact]
    public async Task Paying_the_minimum_avoids_the_late_fee_but_not_interest()
    {
        var card = await IssueToAlice();
        await Buy(card, 10_000m);
        var s1 = await Generate(card);
        Assert.Equal(9_900m, s1.ClosingBalance);
        Assert.Equal(495m, s1.MinimumDue);
        await Pay(card, 495m);

        _services.Clock.Advance(AfterDueDate);
        var s2 = await Generate(card);

        Assert.Equal(nameof(StatementStatus.MinimumPaid), (await _billing.GetStatementAsync(s1.StatementId)).Statement.Status);
        Assert.Equal(388.43m, s2.FeesAndCharges);       // interest 3.5 % of 9,405 = 329.18 + GST 59.25
        Assert.DoesNotContain(await _db.Transactions.ToListAsync(), t => t.TransactionType == TransactionType.Fee);
        Assert.Equal(9_793.43m, s2.ClosingBalance);
        Assert.Equal(await BillableBalanceAsync(card.Card.CardId), s2.ClosingBalance);
    }

    [Fact]
    public async Task Paying_nothing_costs_late_fee_and_interest_and_raises_the_next_minimum()
    {
        var card = await IssueToAlice();
        await Buy(card, 10_000m);
        await Generate(card);

        _services.Clock.Advance(AfterDueDate);
        var s2 = await Generate(card);

        decimal Charged(TransactionType type) =>
            _db.Transactions.Where(t => t.TransactionType == type).Sum(t => t.Amount);
        Assert.Equal(600m, Charged(TransactionType.Fee));               // late fee for 9,900 unpaid
        Assert.Equal(346.50m, Charged(TransactionType.Interest));       // 3.5 % of 9,900
        Assert.Equal(108m + 62.37m, Charged(TransactionType.Tax));      // GST on both
        Assert.Equal(11_016.87m, s2.ClosingBalance);
        Assert.Equal(2_106.87m, s2.MinimumDue);                         // 495 + charges 1,116.87 + 495 past due
        Assert.Contains(await _db.Notifications.Select(n => n.Title).ToListAsync(), t => t == "Payment overdue");
    }

    [Fact]
    public async Task Outcome_waits_for_the_due_date_then_the_scheduler_charges_it()
    {
        var card = await IssueToAlice();
        await Buy(card, 10_000m);
        var s1 = await Generate(card);

        _services.Clock.Advance(TimeSpan.FromDays(1));
        var early = await Generate(card);                                 // s1's due date has not passed
        Assert.Equal(0m, early.FeesAndCharges);
        Assert.Empty(await _billing.GetStatementsToAssessAsync());

        _services.Clock.Advance(TimeSpan.FromDays(19) + TimeSpan.FromHours(1)); // s1's due date passed, the early one's not
        Assert.Equal(new[] { s1.StatementId }, await _billing.GetStatementsToAssessAsync());
        await _billing.AssessStatementAsync(s1.StatementId);

        var s3 = await Generate(card);
        Assert.Equal(1_116.87m, s3.FeesAndCharges);                       // charged once, billed on the next statement
        Assert.Equal(await BillableBalanceAsync(card.Card.CardId), s3.ClosingBalance);
    }

    [Fact]
    public async Task Cash_pays_a_fee_at_once_and_interest_from_day_one()
    {
        var card = await IssueToAlice(limit: 10_000m);

        var tooMuch = await WithdrawCash(card, 9_500m);                   // 9,500 + 500 + 90 > 10,000
        Assert.Equal(DeclineReasons.InsufficientCredit, tooMuch.DeclineReason);

        var cash = await WithdrawCash(card, 9_000m);
        Assert.True(cash.Approved);
        Assert.Equal(410m, (await _db.CreditCards.AsNoTracking().SingleAsync()).AvailableBalance);

        _services.Clock.Advance(TimeSpan.FromDays(10));
        var s = await Generate(card);
        Assert.Equal(590m + 105m + 18.90m, s.FeesAndCharges);             // fee + GST, 10 days' interest + GST
        Assert.Equal(0m, s.Cashback);
    }

    [Fact]
    public async Task Charges_can_take_the_card_over_its_limit()
    {
        var card = await IssueToAlice(limit: 1_000m);
        await Buy(card, 1_000m);                                          // cashback 10 → available 10
        await Generate(card);

        _services.Clock.Advance(AfterDueDate);
        await Generate(card);                                             // late fee 500 + 90, interest 34.65 + 6.24

        Assert.Equal(-620.89m, (await _db.CreditCards.AsNoTracking().SingleAsync()).AvailableBalance);
        Assert.Equal(DeclineReasons.InsufficientCredit, (await Buy(card, 1m)).DeclineReason);
    }

    [Fact]
    public async Task Scheduler_generates_when_the_cycle_ends_and_reminds_once()
    {
        var card = await IssueToAlice();
        await Buy(card, 5_000m);
        Assert.Empty(await _billing.GetCardsDueForStatementAsync());

        _services.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(new[] { card.Card.CardId }, await _billing.GetCardsDueForStatementAsync());
        await _billing.GenerateScheduledStatementAsync(card.Card.CardId);
        Assert.Empty(await _billing.GetCardsDueForStatementAsync());

        _services.Clock.Advance(TimeSpan.FromDays(18));                   // due in 2 days
        Assert.Equal(1, await _billing.SendPaymentRemindersAsync());
        Assert.Equal(0, await _billing.SendPaymentRemindersAsync());      // once per statement
        Assert.Contains(await _db.Notifications.Select(n => n.Title).ToListAsync(), t => t.StartsWith("Payment due on"));
    }

    [Fact]
    public async Task Scheduler_skips_a_card_the_bank_closed_a_moment_ago()
    {
        var card = await IssueToAlice();
        await Buy(card, 5_000m);
        _services.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(new[] { card.Card.CardId }, await _billing.GetCardsDueForStatementAsync());

        await Generate(card);                                              // the bank's button, between the scheduler's steps
        await _billing.GenerateScheduledStatementAsync(card.Card.CardId);  // finds the cycle already closed

        Assert.Single(await _billing.GetStatementsAsync(card.Card.CardId));
    }

    [Fact]
    public async Task Two_billing_runs_at_the_same_moment_cannot_both_close_the_cycle()
    {
        var card = await IssueToAlice();
        await Buy(card, 5_000m);

        // The scheduler and the bank's button read the same card at the same moment...
        using var ctx1 = NewContext();
        using var ctx2 = NewContext();
        var copy1 = await ctx1.CreditCards.SingleAsync();
        var copy2 = await ctx2.CreditCards.SingleAsync();
        var now = DateTime.UtcNow;
        copy1.MarkStatementGenerated(now);
        copy2.MarkStatementGenerated(now.AddSeconds(1));

        await ctx1.SaveChangesAsync();                                                          // first wins
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ctx2.SaveChangesAsync()); // second starts again
    }

    [Fact]
    public async Task Summary_shows_what_is_left_to_pay()
    {
        var card = await IssueToAlice();
        await Buy(card, 10_000m);
        await Generate(card);
        await Pay(card, 300m);
        await Buy(card, 700m);

        var summary = await _billing.GetSummaryAsync(card.Card.CardId);

        Assert.Equal(300m, summary.PaidSinceStatement);
        Assert.Equal(195m, summary.RemainingMinimumDue);
        Assert.Equal(9_600m, summary.RemainingTotalDue);
        Assert.Equal(393m, summary.UnbilledAmount);                       // 700 − 7 cashback − 300 payment
        Assert.False(summary.IsOverdue);

        _services.Clock.Advance(AfterDueDate);
        Assert.True((await _billing.GetSummaryAsync(card.Card.CardId)).IsOverdue);
    }

    [Fact]
    public async Task Only_the_bank_closes_a_cycle_and_people_see_only_their_own()
    {
        var card = await IssueToAlice();
        await Buy(card, 1_000m);

        await Assert.ThrowsAsync<ForbiddenException>(() => _billing.GenerateStatementAsync(card.Card.CardId));
        var s = await Generate(card);

        _user.UserId = _bobId;
        await Assert.ThrowsAsync<NotFoundException>(() => _billing.GetStatementsAsync(card.Card.CardId));
        await Assert.ThrowsAsync<NotFoundException>(() => _billing.GetStatementAsync(s.StatementId));

        AsAlice();
        var pdf = await _billing.GetStatementPdfAsync(s.StatementId);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        Assert.StartsWith($"statement-{card.CardNumber[^4..]}-", pdf.FileName);
    }
}
