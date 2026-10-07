using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 2 flows against EF Core in-memory with the real crypto services.</summary>
public class TransactionServiceTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly CardService _cardService;
    private readonly TransactionService _sut;
    private readonly int _aliceId;
    private readonly int _bobId;

    public TransactionServiceTests()
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

        _cardService = new CardService(
            new CreditCardRepository(_db), new CardholderRepository(_db), _db,
            new AesGcmCardEncryptionService(TestKeys.Encryption()), lookup, hasher,
            new CardNumberGenerator(), _user,
            new IssueCardRequestValidator(), new UpdateCreditLimitRequestValidator(),
            new ChangePinRequestValidator(), new RevealCardNumberRequestValidator());

        _sut = new TransactionService(
            new CreditCardRepository(_db), new TransactionRepository(_db),
            new CashbackRepository(_db), new CashbackEngine(Options.Create(new CashbackOptions())), _db,
            lookup, hasher, _user,
            new SwipeRequestValidator(), new LoadRequestValidator());
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    /// <summary>Admin issues a card to Alice, then the "session" switches to Alice.</summary>
    private async Task<IssuedCardResponse> IssueToAlice(decimal limit = 1_000m)
    {
        _user.IsAdmin = true;
        var issued = await _cardService.IssueCardAsync(new IssueCardRequest(_aliceId, limit));
        _user.IsAdmin = false;
        _user.UserId = _aliceId;
        return issued;
    }

    private static SwipeRequest Swipe(IssuedCardResponse c, decimal amount, string? pin = null, string? cvv = null,
                                      int? month = null) =>
        new(c.CardNumber, month ?? c.Card.ExpiryDate.Month, c.Card.ExpiryDate.Year, cvv ?? c.Cvv,
            pin ?? c.InitialPin, "Big Bazaar", MerchantCategoryCodes.Groceries, amount);

    private static string WrongPin(string pin) => pin == "2580" ? "1357" : "2580";

    [Fact]
    public async Task Approved_swipe_debits_card_and_writes_ledger()
    {
        var card = await IssueToAlice(1_000m);

        var result = await _sut.SwipeAsync(Swipe(card, 250m));

        Assert.True(result.Approved);
        Assert.Equal(7.50m, result.CashbackAmount);           // groceries: 3 % of 250 (Module 3)
        Assert.Equal(757.50m, result.AvailableBalance);       // 1,000 - 250 + 7.50
        var txn = await _db.Transactions.SingleAsync();
        Assert.Equal(TransactionType.Swipe, txn.TransactionType);
        Assert.Equal(TransactionStatus.Completed, txn.TransactionStatus);
    }

    [Fact]
    public async Task Insufficient_credit_is_declined_and_recorded_without_debit()
    {
        var card = await IssueToAlice(1_000m);

        var result = await _sut.SwipeAsync(Swipe(card, 1_000.50m));

        Assert.False(result.Approved);
        Assert.Equal(DeclineReasons.InsufficientCredit, result.DeclineReason);
        Assert.Equal(1_000m, (await _db.CreditCards.SingleAsync()).AvailableBalance);
        Assert.Equal(TransactionStatus.Declined, (await _db.Transactions.SingleAsync()).TransactionStatus);
    }

    [Fact]
    public async Task Wrong_cvv_or_expiry_gives_generic_reason()
    {
        var card = await IssueToAlice();
        var wrongCvv = card.Cvv == "123" ? "124" : "123";

        var r1 = await _sut.SwipeAsync(Swipe(card, 10m, cvv: wrongCvv));
        var r2 = await _sut.SwipeAsync(Swipe(card, 10m, month: card.Card.ExpiryDate.Month % 12 + 1));

        Assert.Equal(DeclineReasons.InvalidCardDetails, r1.DeclineReason);
        Assert.Equal(DeclineReasons.InvalidCardDetails, r2.DeclineReason);
    }

    [Fact]
    public async Task Three_wrong_pins_block_the_card()
    {
        var card = await IssueToAlice();
        var wrong = WrongPin(card.InitialPin);

        Assert.Equal(DeclineReasons.IncorrectPin, (await _sut.SwipeAsync(Swipe(card, 10m, pin: wrong))).DeclineReason);
        Assert.Equal(DeclineReasons.IncorrectPin, (await _sut.SwipeAsync(Swipe(card, 10m, pin: wrong))).DeclineReason);
        Assert.Equal(DeclineReasons.PinTriesExceeded, (await _sut.SwipeAsync(Swipe(card, 10m, pin: wrong))).DeclineReason);

        // Even the correct PIN no longer works.
        Assert.Equal(DeclineReasons.CardBlocked, (await _sut.SwipeAsync(Swipe(card, 10m))).DeclineReason);
        Assert.Equal(CardStatus.Blocked, (await _db.CreditCards.SingleAsync()).CardStatus);
    }

    [Fact]
    public async Task Correct_pin_resets_the_failed_attempt_counter()
    {
        var card = await IssueToAlice();
        await _sut.SwipeAsync(Swipe(card, 10m, pin: WrongPin(card.InitialPin)));
        await _sut.SwipeAsync(Swipe(card, 10m));

        Assert.Equal(0, (await _db.CreditCards.SingleAsync()).FailedPinAttempts);
    }

    [Fact]
    public async Task Wrong_pins_on_reveal_also_count_towards_lockout()
    {
        var card = await IssueToAlice();
        var wrong = new RevealCardNumberRequest(WrongPin(card.InitialPin));

        await Assert.ThrowsAsync<UnauthorizedException>(() => _cardService.RevealCardNumberAsync(card.Card.CardId, wrong));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _cardService.RevealCardNumberAsync(card.Card.CardId, wrong));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _cardService.RevealCardNumberAsync(card.Card.CardId, wrong));

        Assert.Equal(CardStatus.Blocked, (await _db.CreditCards.SingleAsync()).CardStatus);
    }

    [Fact]
    public async Task Cardholder_cannot_swipe_someone_elses_card()
    {
        var card = await IssueToAlice();
        _user.UserId = _bobId;

        var result = await _sut.SwipeAsync(Swipe(card, 10m));

        Assert.Equal(DeclineReasons.InvalidCardDetails, result.DeclineReason);
        Assert.Empty(_db.Transactions); // nothing is revealed or recorded
    }

    [Fact]
    public async Task Load_repays_but_never_beyond_the_limit()
    {
        var card = await IssueToAlice(1_000m);
        await _sut.SwipeAsync(Swipe(card, 400m));

        // 1,000 - 400 + 12 cashback (3 %) = 612 available, 388 owed
        var loaded = await _sut.LoadAsync(new LoadRequest(card.Card.CardId, 150m));
        Assert.Equal(762m, loaded.Card.AvailableBalance);
        Assert.Equal(nameof(TransactionType.Load), loaded.Transaction.TransactionType);

        // 238 still owed - paying 238.01 is too much
        await Assert.ThrowsAsync<DomainException>(() => _sut.LoadAsync(new LoadRequest(card.Card.CardId, 238.01m)));
    }

    [Fact]
    public async Task Refund_restores_balance_and_marks_original_refunded()
    {
        var card = await IssueToAlice(1_000m);
        var swipe = await _sut.SwipeAsync(Swipe(card, 300m));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.RefundAsync(swipe.TransactionId!.Value));

        _user.IsAdmin = true;
        var refunded = await _sut.RefundAsync(swipe.TransactionId!.Value);

        Assert.Equal(1_000m, refunded.Card.AvailableBalance);
        var original = await _db.Transactions.FindAsync(swipe.TransactionId);
        Assert.Equal(TransactionStatus.Refunded, original!.TransactionStatus);
        await Assert.ThrowsAsync<DomainException>(() => _sut.RefundAsync(swipe.TransactionId!.Value));
    }

    [Fact]
    public async Task History_is_paged_newest_first_and_private()
    {
        var card = await IssueToAlice(1_000m);
        for (int i = 1; i <= 5; i++) await _sut.SwipeAsync(Swipe(card, i));

        var page1 = await _sut.GetCardTransactionsAsync(card.Card.CardId, page: 1, pageSize: 2);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(new[] { 5m, 4m }, page1.Items.Select(t => t.Amount));

        _user.UserId = _bobId;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetCardTransactionsAsync(card.Card.CardId, 1, 10));
    }

    [Fact]
    public async Task Concurrent_update_of_the_same_card_is_detected()
    {
        var card = await IssueToAlice(1_000m);

        // Two requests read the same card at the same time...
        using var ctx1 = NewContext();
        using var ctx2 = NewContext();
        var copy1 = await ctx1.CreditCards.SingleAsync();
        var copy2 = await ctx2.CreditCards.SingleAsync();

        // ...both spend 800 of the 1,000 available.
        copy1.Debit(800m);
        copy2.Debit(800m);

        await ctx1.SaveChangesAsync();                                                  // first wins
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ctx2.SaveChangesAsync()); // second is rejected

        Assert.Equal(200m, (await NewContext().CreditCards.SingleAsync()).AvailableBalance);
    }
}

public class RefundAfterRepaymentTests
{
    [Fact]
    public void Refund_of_an_already_repaid_purchase_creates_a_credit_balance()
    {
        var card = new CreditCard(1, "enc", "hash", "XXXX-XXXX-XXXX-1234", "cvv", "pin", 1_000m,
                                  DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));
        card.Debit(300m);
        card.Credit(300m);        // customer repaid everything
        card.CreditRefund(300m);  // then the merchant refunds the purchase

        Assert.Equal(1_300m, card.AvailableBalance);
        Assert.Equal(-300m, card.OutstandingAmount);       // bank owes the customer 300
        Assert.Throws<DomainException>(() => card.Credit(1m)); // cannot "repay" a credit balance
    }
}
