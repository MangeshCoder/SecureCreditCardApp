using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Exercises CardService against the EF Core in-memory provider and the real security services.</summary>
public class CardServiceTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly CardService _sut;
    private readonly int _cardholderId;
    private readonly int _otherCardholderId;

    public CardServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        var bob = new Cardholder("Bob", "B", "bob@test.com", "+911234568", "hash");
        _db.Cardholders.AddRange(alice, bob);
        _db.SaveChanges();
        _cardholderId = alice.CardholderId;
        _otherCardholderId = bob.CardholderId;

        _sut = new CardService(
            new CreditCardRepository(_db), new CardholderRepository(_db), _db,
            new AesGcmCardEncryptionService(TestKeys.Encryption()),
            new HmacCardLookupHasher(TestKeys.Encryption()),
            new PepperedSecretHasher(TestKeys.Encryption(), iterations: 1_000),
            new CardNumberGenerator(), _user,
            new IssueCardRequestValidator(), new UpdateCreditLimitRequestValidator(),
            new ChangePinRequestValidator(), new RevealCardNumberRequestValidator());
    }

    private async Task<IssuedCardResponse> IssueAsAdmin(decimal limit = 50_000m)
    {
        _user.IsAdmin = true;
        var issued = await _sut.IssueCardAsync(new IssueCardRequest(_cardholderId, limit));
        _user.IsAdmin = false;
        _user.UserId = _cardholderId;
        return issued;
    }

    [Fact]
    public async Task Issue_stores_only_encrypted_and_hashed_secrets()
    {
        var issued = await IssueAsAdmin();

        var stored = await _db.CreditCards.SingleAsync();
        Assert.DoesNotContain(issued.CardNumber, stored.CardNumberEncrypted);
        Assert.StartsWith("HPBKDF2-SHA256$", stored.CvvHash);
        Assert.StartsWith("HPBKDF2-SHA256$", stored.PinHash);
        Assert.Equal($"XXXX-XXXX-XXXX-{issued.CardNumber[^4..]}", stored.MaskedCardNumber);
        Assert.Equal(50_000m, stored.AvailableBalance);
    }

    [Fact]
    public async Task Only_admin_can_issue()
    {
        _user.UserId = _cardholderId;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.IssueCardAsync(new IssueCardRequest(_cardholderId, 1_000m)));
    }

    [Fact]
    public async Task Cardholder_cannot_see_someone_elses_card()
    {
        var issued = await IssueAsAdmin();
        _user.UserId = _otherCardholderId;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetCardAsync(issued.Card.CardId));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.BlockCardAsync(issued.Card.CardId));
    }

    [Fact]
    public async Task Change_pin_requires_current_pin_and_then_reveal_works_with_new_pin()
    {
        var issued = await IssueAsAdmin();
        var cardId = issued.Card.CardId;

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.ChangePinAsync(cardId, new ChangePinRequest(issued.InitialPin == "2468" ? "1357" : "2468", "2580")));

        await _sut.ChangePinAsync(cardId, new ChangePinRequest(issued.InitialPin, "2580"));

        var revealed = await _sut.RevealCardNumberAsync(cardId, new RevealCardNumberRequest("2580"));
        Assert.Equal(issued.CardNumber, revealed.CardNumber);
    }

    [Fact]
    public async Task Weak_pin_is_rejected_by_validation()
    {
        var issued = await IssueAsAdmin();
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            _sut.ChangePinAsync(issued.Card.CardId, new ChangePinRequest(issued.InitialPin, "1111")));
    }

    [Fact]
    public async Task Cardholder_can_block_but_only_admin_can_unblock()
    {
        var issued = await IssueAsAdmin();
        var blocked = await _sut.BlockCardAsync(issued.Card.CardId);
        Assert.Equal(nameof(CardStatus.Blocked), blocked.CardStatus);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ActivateCardAsync(issued.Card.CardId));

        _user.IsAdmin = true;
        var active = await _sut.ActivateCardAsync(issued.Card.CardId);
        Assert.Equal(nameof(CardStatus.Active), active.CardStatus);
    }
}
