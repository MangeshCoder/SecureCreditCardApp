using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 3: cashback is earned by swipes, reversed by refunds, and reported per card.</summary>
public class CashbackFlowTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly CardService _cards;
    private readonly TransactionService _transactions;
    private readonly CashbackService _sut;
    private readonly int _aliceId;
    private readonly int _bobId;

    public CashbackFlowTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        var bob = new Cardholder("Bob", "B", "bob@test.com", "+911234568", "hash");
        _db.Cardholders.AddRange(alice, bob);
        _db.SaveChanges();
        _aliceId = alice.CardholderId;
        _bobId = bob.CardholderId;

        var hasher = new PepperedSecretHasher(TestKeys.Encryption(), iterations: 1_000);
        var lookup = new HmacCardLookupHasher(TestKeys.Encryption());
        var engine = new CashbackEngine(Options.Create(new CashbackOptions()));

        _cards = new CardService(new CreditCardRepository(_db), new CardholderRepository(_db), _db,
            new AesGcmCardEncryptionService(TestKeys.Encryption()), lookup, hasher, new CardNumberGenerator(), _user,
            new IssueCardRequestValidator(), new UpdateCreditLimitRequestValidator(),
            new ChangePinRequestValidator(), new RevealCardNumberRequestValidator());
        _transactions = new TransactionService(new CreditCardRepository(_db), new TransactionRepository(_db),
            new CashbackRepository(_db), engine, new EmiPlanRepository(_db), _db, lookup, hasher, _user,
            new CardControlRules(Options.Create(new CardControlOptions())),
            new SwipeRequestValidator(), new LoadRequestValidator());
        _sut = new CashbackService(engine, new CashbackRepository(_db), new CreditCardRepository(_db), _user);
    }

    private async Task<IssuedCardResponse> IssueToAlice(decimal limit = 100_000m)
    {
        _user.IsAdmin = true;
        var issued = await _cards.IssueCardAsync(new IssueCardRequest(_aliceId, limit));
        _user.IsAdmin = false;
        _user.UserId = _aliceId;
        return issued;
    }

    private Task<SwipeResponse> Swipe(IssuedCardResponse c, decimal amount, string mcc, string? pin = null) =>
        _transactions.SwipeAsync(new SwipeRequest(c.CardNumber, c.Card.ExpiryDate.Month, c.Card.ExpiryDate.Year,
                                                  c.Cvv, pin ?? c.InitialPin, "Shop", mcc, amount));

    [Fact]
    public async Task Approved_swipe_earns_cashback_as_statement_credit()
    {
        var card = await IssueToAlice(10_000m);

        var result = await Swipe(card, 2_000m, MerchantCategoryCodes.Restaurants);

        Assert.Equal(60m, result.CashbackAmount);                 // 3 % dining
        Assert.Equal(3m, result.CashbackPercentage);
        Assert.Equal(8_060m, result.AvailableBalance);            // 10,000 - 2,000 + 60
        var log = await _db.CashbackLogs.SingleAsync();
        Assert.Equal(CashbackType.Earned, log.CashbackType);
        Assert.Equal(result.TransactionId, log.TransactionId);    // FK filled in by EF in the same save
    }

    [Fact]
    public async Task Declined_or_small_swipes_earn_nothing()
    {
        var card = await IssueToAlice(1_000m);

        await Swipe(card, 5_000m, MerchantCategoryCodes.Groceries);                  // declined: insufficient credit
        await Swipe(card, 50m, MerchantCategoryCodes.Groceries);                     // below minimum spend
        await Swipe(card, 500m, MerchantCategoryCodes.Groceries, pin: "0000" == card.InitialPin ? "1357" : "0000"); // wrong PIN

        Assert.Empty(_db.CashbackLogs);
    }

    [Fact]
    public async Task Refund_reverses_the_cashback_exactly_once()
    {
        var card = await IssueToAlice(10_000m);
        var swipe = await Swipe(card, 1_000m, MerchantCategoryCodes.FuelStations); // 20 cashback

        _user.IsAdmin = true;
        var refunded = await _transactions.RefundAsync(swipe.TransactionId!.Value);

        Assert.Equal(10_000m, refunded.Card.AvailableBalance);    // purchase AND its cashback undone
        var logs = await _db.CashbackLogs.OrderBy(l => l.CashbackId).ToListAsync();
        Assert.Equal(new[] { 20m, -20m }, logs.Select(l => l.CashbackAmount));
        Assert.Equal(CashbackType.Reversed, logs[1].CashbackType);
        Assert.Equal(swipe.TransactionId, logs[1].TransactionId);  // reversal points at the original swipe
    }

    [Fact]
    public async Task Summary_groups_by_category_and_nets_out_reversals()
    {
        var card = await IssueToAlice();
        await Swipe(card, 1_000m, MerchantCategoryCodes.Groceries);      // 30
        await Swipe(card, 2_000m, MerchantCategoryCodes.Groceries);      // 60
        await Swipe(card, 1_000m, MerchantCategoryCodes.FuelStations);   // 20
        var tv = await Swipe(card, 10_000m, MerchantCategoryCodes.Electronics); // 100 (1 %)

        _user.IsAdmin = true;
        await _transactions.RefundAsync(tv.TransactionId!.Value);        // -100

        var summary = await _sut.GetCardSummaryAsync(card.Card.CardId);

        Assert.Equal(210m, summary.TotalEarned);
        Assert.Equal(-100m, summary.TotalReversed);
        Assert.Equal(110m, summary.NetCashback);
        Assert.Equal(110m, summary.ThisMonth);
        var groceries = summary.ByCategory.First();
        Assert.Equal(MerchantCategoryCodes.Groceries, groceries.MerchantCategoryCode);
        Assert.Equal(90m, groceries.Amount);
        Assert.Equal(2, groceries.TransactionCount);
        Assert.Equal(0m, summary.ByCategory.Single(c => c.MerchantCategoryCode == MerchantCategoryCodes.Electronics).Amount);
    }

    [Fact]
    public async Task Statement_rows_show_cashback_and_reversal()
    {
        var card = await IssueToAlice();
        var kept = await Swipe(card, 1_000m, MerchantCategoryCodes.Groceries);
        var refunded = await Swipe(card, 1_000m, MerchantCategoryCodes.Restaurants);
        _user.IsAdmin = true;
        await _transactions.RefundAsync(refunded.TransactionId!.Value);

        var rows = (await _transactions.GetCardTransactionsAsync(card.Card.CardId, 1, 10)).Items;

        var keptRow = rows.Single(r => r.TransactionId == kept.TransactionId);
        Assert.Equal(30m, keptRow.CashbackEarned);
        Assert.False(keptRow.CashbackReversed);
        Assert.True(rows.Single(r => r.TransactionId == refunded.TransactionId).CashbackReversed);
    }

    [Fact]
    public async Task Cashback_of_another_customers_card_is_hidden()
    {
        var card = await IssueToAlice();
        _user.UserId = _bobId;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetCardSummaryAsync(card.Card.CardId));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetCardHistoryAsync(card.Card.CardId, 1, 10));
    }

    [Fact]
    public void Rules_are_exposed_highest_rate_first()
    {
        var rules = _sut.GetRules();
        Assert.Equal(new[] { "5411", "5812", "5541" }, rules.CategoryRules.Select(r => r.MerchantCategoryCode));
        Assert.Equal(1m, rules.DefaultPercentage);
    }
}
