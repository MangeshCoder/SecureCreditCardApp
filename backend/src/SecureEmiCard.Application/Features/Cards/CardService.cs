using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Cards;

public interface ICardService
{
    Task<IssuedCardResponse> IssueCardAsync(IssueCardRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CardDto>> GetMyCardsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CardDto>> GetAllCardsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CardDto>> GetCardsOfCardholderAsync(int cardholderId, CancellationToken ct = default);
    Task<CardDto> GetCardAsync(int cardId, CancellationToken ct = default);
    Task<CardDto> BlockCardAsync(int cardId, CancellationToken ct = default);
    Task<CardDto> ActivateCardAsync(int cardId, CancellationToken ct = default);
    Task<CardDto> UpdateCreditLimitAsync(int cardId, UpdateCreditLimitRequest request, CancellationToken ct = default);
    Task ChangePinAsync(int cardId, ChangePinRequest request, CancellationToken ct = default);
    Task<RevealCardNumberResponse> RevealCardNumberAsync(int cardId, RevealCardNumberRequest request, CancellationToken ct = default);
}

public class CardService : ICardService
{
    private const int CardValidityYears = 5;

    private readonly ICreditCardRepository _cards;
    private readonly ICardholderRepository _cardholders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICardEncryptionService _encryption;
    private readonly ICardLookupHasher _lookupHasher;
    private readonly ISecretHasher _secretHasher;
    private readonly ICardNumberGenerator _generator;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<IssueCardRequest> _issueValidator;
    private readonly IValidator<UpdateCreditLimitRequest> _limitValidator;
    private readonly IValidator<ChangePinRequest> _pinValidator;
    private readonly IValidator<RevealCardNumberRequest> _revealValidator;

    public CardService(ICreditCardRepository cards, ICardholderRepository cardholders, IUnitOfWork unitOfWork,
                       ICardEncryptionService encryption, ICardLookupHasher lookupHasher,
                       ISecretHasher secretHasher, ICardNumberGenerator generator,
                       ICurrentUser currentUser,
                       IValidator<IssueCardRequest> issueValidator,
                       IValidator<UpdateCreditLimitRequest> limitValidator,
                       IValidator<ChangePinRequest> pinValidator,
                       IValidator<RevealCardNumberRequest> revealValidator)
    {
        _cards = cards;
        _cardholders = cardholders;
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _lookupHasher = lookupHasher;
        _secretHasher = secretHasher;
        _generator = generator;
        _currentUser = currentUser;
        _issueValidator = issueValidator;
        _limitValidator = limitValidator;
        _pinValidator = pinValidator;
        _revealValidator = revealValidator;
    }

    public async Task<IssuedCardResponse> IssueCardAsync(IssueCardRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        await _issueValidator.ValidateAndThrowAsync(request, ct);

        var cardholder = await _cardholders.GetByIdAsync(request.CardholderId, ct)
                         ?? throw new NotFoundException($"Cardholder {request.CardholderId} was not found.");
        if (!cardholder.IsActive)
            throw new ConflictException("Cannot issue a card to an inactive cardholder.");
        if (cardholder.Role != UserRole.Cardholder)
            throw new ConflictException("Cards can only be issued to cardholder accounts.");

        // Secrets exist in clear text only in memory, for the duration of this request.
        // The blind index lets us guarantee the generated number is not already in use.
        string cardNumber, cardNumberHash;
        do
        {
            cardNumber = _generator.GenerateCardNumber();
            cardNumberHash = _lookupHasher.Compute(cardNumber);
        } while (await _cards.NumberHashExistsAsync(cardNumberHash, ct));

        var cvv = _generator.GenerateCvv();
        var pin = _generator.GeneratePin();

        var card = new CreditCard(
            cardholderId: cardholder.CardholderId,
            cardNumberEncrypted: _encryption.Encrypt(cardNumber),
            cardNumberHash: cardNumberHash,
            maskedCardNumber: CardNumberGenerator.Mask(cardNumber),
            cvvHash: _secretHasher.Hash(cvv),
            pinHash: _secretHasher.Hash(pin),
            creditLimit: request.CreditLimit,
            expiryDate: EndOfMonth(DateTime.UtcNow.AddYears(CardValidityYears)));

        await _cards.AddAsync(card, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new IssuedCardResponse(card.ToDto(), cardNumber, cvv, pin);
    }

    public async Task<IReadOnlyList<CardDto>> GetMyCardsAsync(CancellationToken ct = default)
        => (await _cards.GetByCardholderAsync(_currentUser.UserId, ct)).Select(c => c.ToDto()).ToList();

    public async Task<IReadOnlyList<CardDto>> GetAllCardsAsync(CancellationToken ct = default)
    {
        EnsureAdmin();
        return (await _cards.GetAllAsync(ct)).Select(c => c.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<CardDto>> GetCardsOfCardholderAsync(int cardholderId, CancellationToken ct = default)
    {
        EnsureAdmin();
        return (await _cards.GetByCardholderAsync(cardholderId, ct)).Select(c => c.ToDto()).ToList();
    }

    public async Task<CardDto> GetCardAsync(int cardId, CancellationToken ct = default)
        => (await GetAccessibleCardAsync(cardId, ct)).ToDto();

    public async Task<CardDto> BlockCardAsync(int cardId, CancellationToken ct = default)
    {
        // A cardholder can block their own card instantly (e.g. lost/stolen); admins can block any card.
        var card = await GetAccessibleCardAsync(cardId, ct);
        card.Block();
        await _unitOfWork.SaveChangesAsync(ct);
        return card.ToDto();
    }

    public async Task<CardDto> ActivateCardAsync(int cardId, CancellationToken ct = default)
    {
        // Unblocking is a bank decision, so only admins may do it.
        EnsureAdmin();
        var card = await GetCardOrThrowAsync(cardId, ct);
        card.Activate();
        await _unitOfWork.SaveChangesAsync(ct);
        return card.ToDto();
    }

    public async Task<CardDto> UpdateCreditLimitAsync(int cardId, UpdateCreditLimitRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        await _limitValidator.ValidateAndThrowAsync(request, ct);
        var card = await GetCardOrThrowAsync(cardId, ct);
        card.UpdateCreditLimit(request.NewCreditLimit);
        await _unitOfWork.SaveChangesAsync(ct);
        return card.ToDto();
    }

    public async Task ChangePinAsync(int cardId, ChangePinRequest request, CancellationToken ct = default)
    {
        await _pinValidator.ValidateAndThrowAsync(request, ct);
        var card = await GetOwnCardAsync(cardId, ct);
        await VerifyPinOrThrowAsync(card, request.CurrentPin, ct);

        card.ChangePin(_secretHasher.Hash(request.NewPin));
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<RevealCardNumberResponse> RevealCardNumberAsync(int cardId, RevealCardNumberRequest request, CancellationToken ct = default)
    {
        await _revealValidator.ValidateAndThrowAsync(request, ct);
        var card = await GetOwnCardAsync(cardId, ct);
        await VerifyPinOrThrowAsync(card, request.Pin, ct);
        await _unitOfWork.SaveChangesAsync(ct); // persists a reset of the failed-attempt counter

        return new RevealCardNumberResponse(card.CardId, _encryption.Decrypt(card.CardNumberEncrypted));
    }

    // ---- helpers -------------------------------------------------------------

    private void EnsureAdmin()
    {
        if (!_currentUser.IsAdmin) throw new ForbiddenException("This operation requires the Admin role.");
    }

    private async Task<CreditCard> GetCardOrThrowAsync(int cardId, CancellationToken ct)
        => await _cards.GetByIdAsync(cardId, ct) ?? throw new NotFoundException($"Card {cardId} was not found.");

    /// <summary>Admins can access any card; cardholders only their own.</summary>
    private async Task<CreditCard> GetAccessibleCardAsync(int cardId, CancellationToken ct)
    {
        var card = await GetCardOrThrowAsync(cardId, ct);
        if (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId)
            throw new NotFoundException($"Card {cardId} was not found."); // 404, not 403: don't reveal that the card exists
        return card;
    }

    /// <summary>PIN operations are restricted to the card owner - not even admins may use them.</summary>
    private async Task<CreditCard> GetOwnCardAsync(int cardId, CancellationToken ct)
    {
        var card = await GetCardOrThrowAsync(cardId, ct);
        if (card.CardholderId != _currentUser.UserId)
            throw new NotFoundException($"Card {cardId} was not found.");
        return card;
    }

    /// <summary>
    /// Applies the 3-strikes PIN rule. A wrong PIN is saved BEFORE throwing, otherwise the
    /// failed-attempt counter would be rolled back and an attacker could guess forever.
    /// </summary>
    private async Task VerifyPinOrThrowAsync(CreditCard card, string pin, CancellationToken ct)
    {
        if (!card.IsActive)
            throw new DomainException("This card is blocked. Contact the bank to unblock it.");

        var result = PinCheck.Verify(_secretHasher, card, pin);
        if (result == PinCheckResult.Valid) return;

        await _unitOfWork.SaveChangesAsync(ct);
        throw new UnauthorizedException(result == PinCheckResult.LockedOut
            ? "Incorrect PIN. The card has been blocked after too many wrong attempts."
            : $"Incorrect PIN. {card.RemainingPinAttempts} attempt(s) left before the card is blocked.");
    }

    private static DateOnly EndOfMonth(DateTime date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
