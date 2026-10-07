using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 4 end to end: purchase -> convert -> pay installments, with all the money effects.</summary>
public class EmiFlowTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly CardService _cards;
    private readonly TransactionService _transactions;
    private readonly EmiService _sut;
    private readonly int _aliceId;
    private readonly int _bobId;

    public EmiFlowTests()
    {
        _db = NewContext();
        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        var bob = new Cardholder("Bob", "B", "bob@test.com", "+911234568", "hash");
        _db.Cardholders.AddRange(alice, bob);
        _db.SaveChanges();
        _aliceId = alice.CardholderId;
        _bobId = bob.CardholderId;

        var hasher = new PepperedSecretHasher(TestKeys.Encryption(), iterations: 1_000);
        var lookup = new HmacCardLookupHasher(TestKeys.Encryption());

        _cards = new CardService(new CreditCardRepository(_db), new CardholderRepository(_db), _db,
            new AesGcmCardEncryptionService(TestKeys.Encryption()), lookup, hasher, new CardNumberGenerator(), _user,
            new IssueCardRequestValidator(), new UpdateCreditLimitRequestValidator(),
            new ChangePinRequestValidator(), new RevealCardNumberRequestValidator());
        _transactions = new TransactionService(new CreditCardRepository(_db), new TransactionRepository(_db),
            new CashbackRepository(_db), new CashbackEngine(Options.Create(new CashbackOptions())),
            new EmiPlanRepository(_db), _db, lookup, hasher, _user,
            new SwipeRequestValidator(), new LoadRequestValidator());
        _sut = new EmiService(new EmiCalculator(Options.Create(new EmiOptions())), new EmiPlanRepository(_db),
            new TransactionRepository(_db), new CreditCardRepository(_db), _db, _user,
            new EmiPreviewRequestValidator(), new ConvertToEmiRequestValidator());
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    /// <summary>Card with 1,00,000 limit for Alice and one 12,000 electronics purchase (1 % cashback = 120).</summary>
    private async Task<(int CardId, int PurchaseId)> AliceBuysTv()
    {
        _user.IsAdmin = true;
        var c = await _cards.IssueCardAsync(new IssueCardRequest(_aliceId, 100_000m));
        _user.IsAdmin = false;
        _user.UserId = _aliceId;
        var swipe = await _transactions.SwipeAsync(new SwipeRequest(c.CardNumber, c.Card.ExpiryDate.Month,
            c.Card.ExpiryDate.Year, c.Cvv, c.InitialPin, "Croma", MerchantCategoryCodes.Electronics, 12_000m));
        Assert.True(swipe.Approved);
        return (c.Card.CardId, swipe.TransactionId!.Value);
    }

    private async Task<decimal> Available(int cardId) =>
        (await NewContext().CreditCards.SingleAsync(c => c.CardId == cardId)).AvailableBalance;

    [Fact]
    public async Task Convert_creates_plan_and_schedule_without_changing_the_balance()
    {
        var (cardId, purchaseId) = await AliceBuysTv();
        var before = await Available(cardId);

        Assert.Contains(await _sut.GetEligibleTransactionsAsync(cardId), e => e.TransactionId == purchaseId);

        var plan = await _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(6));

        Assert.Equal(12_000m, plan.PrincipalAmount);
        Assert.Equal(14m, plan.AnnualInterestRate);                  // bank's rate for 6 months
        Assert.Equal(6, plan.Schedule.Count);
        Assert.Equal(12_000m, plan.Schedule.Sum(s => s.PrincipalComponent));
        Assert.Equal(before, await Available(cardId));               // converting moves no money
        Assert.True((await NewContext().Transactions.FindAsync(purchaseId))!.IsEmiConverted);
        Assert.Empty(await _sut.GetEligibleTransactionsAsync(cardId));
    }

    [Fact]
    public async Task A_purchase_cannot_be_converted_twice_or_refunded_after_conversion()
    {
        var (_, purchaseId) = await AliceBuysTv();
        await _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(3));

        await Assert.ThrowsAsync<DomainException>(() => _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(3)));
        _user.IsAdmin = true;
        await Assert.ThrowsAsync<DomainException>(() => _transactions.RefundAsync(purchaseId));
    }

    [Fact]
    public async Task Paying_an_installment_releases_its_principal_and_writes_the_ledger()
    {
        var (cardId, purchaseId) = await AliceBuysTv();
        var plan = await _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(3));
        var first = plan.Schedule[0];
        var before = await Available(cardId);

        await Assert.ThrowsAsync<DomainException>(() => _sut.PayInstallmentAsync(plan.EmiPlanId, 2)); // must pay #1 first

        var paid = await _sut.PayInstallmentAsync(plan.EmiPlanId, 1);

        Assert.Equal(before + first.PrincipalComponent, paid.Card.AvailableBalance); // interest does not free the limit
        Assert.Equal(1, paid.Plan.PaidInstallments);
        Assert.Equal(plan.TotalRepayable - first.AmountDue, paid.Plan.RemainingBalance);
        var ledger = await NewContext().Transactions.FindAsync(paid.TransactionId);
        Assert.Equal(TransactionType.EmiInstallment, ledger!.TransactionType);
        Assert.Equal(first.AmountDue, ledger.Amount);
        var schedule = await NewContext().EmiSchedules.SingleAsync(s => s.EmiPlanId == plan.EmiPlanId && s.InstallmentNumber == 1);
        Assert.Equal(paid.TransactionId, schedule.PaymentTransactionId);

        // the same request again (double click) does not pay installment 2
        await Assert.ThrowsAsync<DomainException>(() => _sut.PayInstallmentAsync(plan.EmiPlanId, 1));
        Assert.Equal(1, (await _sut.GetPlanAsync(plan.EmiPlanId)).PaidInstallments);
    }

    [Fact]
    public async Task Paying_every_installment_closes_the_plan_and_frees_the_whole_principal()
    {
        var (cardId, purchaseId) = await AliceBuysTv();
        var before = await Available(cardId);
        var plan = await _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(3));

        for (int n = 1; n <= 3; n++) await _sut.PayInstallmentAsync(plan.EmiPlanId, n);

        var closed = await _sut.GetPlanAsync(plan.EmiPlanId);
        Assert.Equal(nameof(EmiPlanStatus.Closed), closed.PlanStatus);
        Assert.Equal(0m, closed.RemainingBalance);
        Assert.Equal(before + 12_000m, await Available(cardId));
        Assert.Equal(0, (await _sut.GetCardSummaryAsync(cardId)).ActivePlans);
    }

    [Fact]
    public async Task Pay_bill_cannot_pay_the_part_that_is_in_emi()
    {
        var (cardId, purchaseId) = await AliceBuysTv();
        await _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(12));

        var summary = await _sut.GetCardSummaryAsync(cardId);
        Assert.Equal(12_000m, summary.OutstandingPrincipal);
        // owed: 12,000 - 120 cashback = 11,880; all of the 12,000 is in EMI -> nothing payable via Pay bill
        Assert.Equal(0m, summary.PayableOutsideEmi);
        await Assert.ThrowsAsync<DomainException>(() => _transactions.LoadAsync(new LoadRequest(cardId, 100m)));
    }

    [Fact]
    public async Task Another_customer_cannot_see_or_convert()
    {
        var (cardId, purchaseId) = await AliceBuysTv();
        var plan = await _sut.ConvertTransactionAsync(purchaseId, new ConvertToEmiRequest(3));
        _user.UserId = _bobId;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetCardPlansAsync(cardId));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetPlanAsync(plan.EmiPlanId));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.PayInstallmentAsync(plan.EmiPlanId, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetEligibleTransactionsAsync(cardId));
    }

    [Fact]
    public async Task Refund_and_conversion_at_the_same_moment_cannot_both_succeed()
    {
        var (_, purchaseId) = await AliceBuysTv();

        using var refundCtx = NewContext();
        using var convertCtx = NewContext();
        var forRefund = await refundCtx.Transactions.SingleAsync(t => t.TransactionId == purchaseId);
        var forConvert = await convertCtx.Transactions.SingleAsync(t => t.TransactionId == purchaseId);

        refundCtx.Transactions.Add(forRefund.Refund());
        forConvert.MarkEmiConverted(100m, 30, DateTime.UtcNow);

        await refundCtx.SaveChangesAsync();                                                       // refund wins
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => convertCtx.SaveChangesAsync()); // conversion rejected
    }

    [Fact]
    public void Preview_and_options_use_the_banks_rates()
    {
        var preview = _sut.Preview(new EmiPreviewRequest(100_000m, 12));
        Assert.Equal(9_025.83m, preview.MonthlyInstallment);

        var options = _sut.GetOptions(100_000m);
        Assert.Equal(new[] { 3, 6, 12, 24 }, options.Select(o => o.TenureMonths));
        Assert.True(options.Zip(options.Skip(1)).All(p => p.First.TotalInterest < p.Second.TotalInterest)); // longer = more interest
    }
}
