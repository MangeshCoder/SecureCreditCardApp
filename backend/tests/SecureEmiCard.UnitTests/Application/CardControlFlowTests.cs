using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 6: card controls applied to real swipes (EF Core in-memory, real crypto).</summary>
public class CardControlFlowTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private static readonly ChannelSettingRequest On = new(true, null);
    private static readonly ChannelSettingRequest Off = new(false, null);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly CardService _cards;
    private readonly TransactionService _transactions;
    private readonly CardControlService _sut;
    private readonly int _aliceId;
    private readonly int _bobId;

    public CardControlFlowTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        var bob = new Cardholder("Bob", "B", "bob@test.com", "+911234568", "hash");
        _db.Cardholders.AddRange(alice, bob);
        _db.SaveChanges();
        _aliceId = alice.CardholderId;
        _bobId = bob.CardholderId;

        var hasher = new PepperedSecretHasher(TestKeys.Encryption(), iterations: 1_000);
        var lookup = new HmacCardLookupHasher(TestKeys.Encryption());
        var rules = new CardControlRules(Options.Create(new CardControlOptions()));

        _cards = new CardService(new CreditCardRepository(_db), new CardholderRepository(_db), _db,
            new AesGcmCardEncryptionService(TestKeys.Encryption()), lookup, hasher, new CardNumberGenerator(), _user,
            new IssueCardRequestValidator(), new UpdateCreditLimitRequestValidator(),
            new ChangePinRequestValidator(), new RevealCardNumberRequestValidator());
        _transactions = new TransactionService(new CreditCardRepository(_db), new TransactionRepository(_db),
            new CashbackRepository(_db), new CashbackEngine(Options.Create(new CashbackOptions())),
            new EmiPlanRepository(_db), _db, lookup, hasher, _user, rules,
            new SwipeRequestValidator(), new LoadRequestValidator());
        _sut = new CardControlService(new CreditCardRepository(_db), new TransactionRepository(_db), _db, _user, rules,
            new UpdateCardControlsRequestValidator());
    }

    private async Task<IssuedCardResponse> IssueToAlice(decimal limit = 100_000m)
    {
        _user.IsAdmin = true;
        var issued = await _cards.IssueCardAsync(new IssueCardRequest(_aliceId, limit));
        _user.IsAdmin = false;
        _user.UserId = _aliceId;
        return issued;
    }

    private Task<SwipeResponse> Swipe(IssuedCardResponse c, decimal amount, TransactionChannel channel = TransactionChannel.Pos,
                                      string? country = null, string mcc = MerchantCategoryCodes.Groceries, string? pin = null) =>
        _transactions.SwipeAsync(new SwipeRequest(c.CardNumber, c.Card.ExpiryDate.Month, c.Card.ExpiryDate.Year, c.Cvv,
                                                  pin ?? c.InitialPin, "Merchant", mcc, amount, channel, country));

    private Task<CardControlsDto> SetControls(IssuedCardResponse c, ChannelSettingRequest? pos = null,
                                              ChannelSettingRequest? online = null, ChannelSettingRequest? contactless = null,
                                              ChannelSettingRequest? atm = null, ChannelSettingRequest? international = null) =>
        _sut.UpdateAsync(c.Card.CardId, new UpdateCardControlsRequest(pos ?? On, online ?? Off, contactless ?? Off,
                                                                      atm ?? On, international ?? Off));

    [Fact]
    public async Task New_card_works_at_shops_but_online_only_after_the_cardholder_switches_it_on()
    {
        var card = await IssueToAlice();

        Assert.True((await Swipe(card, 500m)).Approved);
        var online = await Swipe(card, 500m, TransactionChannel.Online);
        Assert.Equal(DeclineReasons.ChannelDisabled(TransactionChannel.Online), online.DeclineReason);
        var declined = await _db.Transactions.SingleAsync(t => t.TransactionId == online.TransactionId);
        Assert.Equal(TransactionChannel.Online, declined.Channel);                    // recorded with its channel

        await SetControls(card, online: On);
        Assert.True((await Swipe(card, 500m, TransactionChannel.Online)).Approved);
    }

    [Fact]
    public async Task Locked_card_is_declined_before_the_pin_so_no_attempt_is_used()
    {
        var card = await IssueToAlice();
        var wrongPin = card.InitialPin == "2580" ? "1357" : "2580";
        await Swipe(card, 1_000m);

        var locked = await _sut.LockAsync(card.Card.CardId);
        Assert.True(locked.IsLocked);
        Assert.Equal(DeclineReasons.CardLocked, (await Swipe(card, 10m, pin: wrongPin)).DeclineReason);
        Assert.Equal(0, (await _db.CreditCards.SingleAsync()).FailedPinAttempts);
        await _transactions.LoadAsync(new LoadRequest(card.Card.CardId, 100m));     // repaying still works

        Assert.False((await _sut.UnlockAsync(card.Card.CardId)).IsLocked);
        Assert.True((await Swipe(card, 10m)).Approved);
    }

    [Fact]
    public async Task International_use_needs_both_switches_and_is_marked_on_the_ledger()
    {
        var card = await IssueToAlice();
        await SetControls(card, online: On);

        var abroad = await Swipe(card, 1_000m, TransactionChannel.Online, "us");
        Assert.Equal(DeclineReasons.InternationalDisabled, abroad.DeclineReason);

        await SetControls(card, online: On, international: On);
        var approved = await Swipe(card, 1_000m, TransactionChannel.Online, "us");
        Assert.True(approved.Approved);
        var txn = await _db.Transactions.SingleAsync(t => t.TransactionId == approved.TransactionId);
        Assert.Equal("US", txn.MerchantCountry);
        Assert.True(txn.IsInternational);

        var home = await Swipe(card, 100m, TransactionChannel.Pos, "in");          // home country, any case
        Assert.False((await _db.Transactions.SingleAsync(t => t.TransactionId == home.TransactionId)).IsInternational);
    }

    [Fact]
    public async Task Daily_limit_counts_only_todays_approved_spend_in_that_channel()
    {
        var card = await IssueToAlice();
        await SetControls(card, pos: new ChannelSettingRequest(true, 1_000m), online: On);

        Assert.True((await Swipe(card, 600m)).Approved);
        Assert.Equal(DeclineReasons.DailyLimitExceeded(TransactionChannel.Pos), (await Swipe(card, 500m)).DeclineReason);
        Assert.True((await Swipe(card, 400m)).Approved);                            // the decline did not count
        Assert.True((await Swipe(card, 500m, TransactionChannel.Online)).Approved); // other channel, own limit

        var controls = await _sut.GetAsync(card.Card.CardId);
        Assert.Equal(1_000m, controls.Pos.SpentToday);
        Assert.Equal(500m, controls.Online.SpentToday);
    }

    [Fact]
    public async Task Refunded_purchase_still_uses_the_daily_limit()
    {
        var card = await IssueToAlice();
        await SetControls(card, pos: new ChannelSettingRequest(true, 1_000m));
        var purchase = await Swipe(card, 800m);

        _user.IsAdmin = true;
        await _transactions.RefundAsync(purchase.TransactionId!.Value);
        _user.IsAdmin = false;

        Assert.Equal(DeclineReasons.DailyLimitExceeded(TransactionChannel.Pos), (await Swipe(card, 300m)).DeclineReason);
    }

    [Fact]
    public async Task Contactless_above_the_cap_is_declined()
    {
        var card = await IssueToAlice();
        await SetControls(card, contactless: On);

        Assert.True((await Swipe(card, 5_000m, TransactionChannel.Contactless)).Approved);
        Assert.Equal(DeclineReasons.ContactlessLimitExceeded,
                     (await Swipe(card, 5_000.01m, TransactionChannel.Contactless)).DeclineReason);
    }

    [Fact]
    public async Task Atm_withdrawal_needs_mcc_6011_and_earns_no_cashback()
    {
        var card = await IssueToAlice();

        var cash = await Swipe(card, 2_000m, TransactionChannel.Atm, mcc: MerchantCategoryCodes.CashWithdrawal);
        Assert.True(cash.Approved);
        Assert.Equal(0m, cash.CashbackAmount);
        Assert.Empty(await _db.CashbackLogs.ToListAsync());

        await Assert.ThrowsAsync<ValidationException>(() => Swipe(card, 100m, TransactionChannel.Atm));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Swipe(card, 100m, TransactionChannel.Pos, mcc: MerchantCategoryCodes.CashWithdrawal));
    }

    [Fact]
    public async Task Only_the_cardholder_can_change_controls_admins_can_look()
    {
        var card = await IssueToAlice();
        var request = new UpdateCardControlsRequest(On, On, Off, On, Off);

        _user.IsAdmin = true;
        Assert.False((await _sut.GetAsync(card.Card.CardId)).Online.Enabled);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.UpdateAsync(card.Card.CardId, request));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.LockAsync(card.Card.CardId));

        _user.IsAdmin = false;
        _user.UserId = _bobId;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(card.Card.CardId));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateAsync(card.Card.CardId, request));
    }

    [Fact]
    public async Task Limits_are_validated_and_a_blocked_card_cannot_be_changed()
    {
        var card = await IssueToAlice(10_000m);

        await Assert.ThrowsAsync<DomainException>(() => SetControls(card, online: new ChannelSettingRequest(true, 20_000m)));
        await Assert.ThrowsAsync<ValidationException>(() => SetControls(card, online: new ChannelSettingRequest(true, -1m)));

        await _cards.BlockCardAsync(card.Card.CardId);
        await Assert.ThrowsAsync<DomainException>(() => SetControls(card, online: On));
    }
}
