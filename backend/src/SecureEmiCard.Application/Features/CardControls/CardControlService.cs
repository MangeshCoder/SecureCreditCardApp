using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.CardControls;

public interface ICardControlService
{
    Task<CardControlsDto> GetAsync(int cardId, CancellationToken ct = default);
    Task<CardControlsDto> UpdateAsync(int cardId, UpdateCardControlsRequest request, CancellationToken ct = default);
    Task<CardDto> LockAsync(int cardId, CancellationToken ct = default);
    Task<CardDto> UnlockAsync(int cardId, CancellationToken ct = default);
}

/// <summary>
/// Self-service card controls (Module 6): channel switches, daily limits and the temporary lock.
/// Only the cardholder may change them. Admins can look (customer support) but not touch - the bank's
/// own tools are Block / Unblock and the credit limit.
/// The rules themselves are applied in TransactionService.AuthorizeAsync for every swipe.
/// </summary>
public class CardControlService : ICardControlService
{
    private readonly ICreditCardRepository _cards;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ICardControlRules _rules;
    private readonly IValidator<UpdateCardControlsRequest> _validator;

    public CardControlService(ICreditCardRepository cards, ITransactionRepository transactions, IUnitOfWork unitOfWork,
                              ICurrentUser currentUser, ICardControlRules rules, IValidator<UpdateCardControlsRequest> validator)
    {
        _cards = cards;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _rules = rules;
        _validator = validator;
    }

    public async Task<CardControlsDto> GetAsync(int cardId, CancellationToken ct = default)
    {
        var card = await _cards.GetByIdAsync(cardId, ct);
        if (card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            throw new NotFoundException($"Card {cardId} was not found.");
        return await ToDtoAsync(card, ct);
    }

    public async Task<CardControlsDto> UpdateAsync(int cardId, UpdateCardControlsRequest request, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var card = await GetOwnCardAsync(cardId, ct);
        if (!card.IsActive) throw new DomainException("This card is blocked. Contact the bank to unblock it.");

        card.EnsureControls().Update(
            ToSetting(request.Pos), ToSetting(request.Online), ToSetting(request.Contactless),
            ToSetting(request.Atm), ToSetting(request.International), card.CreditLimit);
        await _unitOfWork.SaveChangesAsync(ct);

        return await ToDtoAsync(card, ct);
    }

    public Task<CardDto> LockAsync(int cardId, CancellationToken ct = default) =>
        ChangeLockAsync(cardId, card => card.Lock(), ct);

    public Task<CardDto> UnlockAsync(int cardId, CancellationToken ct = default) =>
        ChangeLockAsync(cardId, card => card.Unlock(), ct);

    // ---- helpers -------------------------------------------------------------

    /// <summary>
    /// The lock is a column of CreditCards, whose AvailableBalance is a concurrency token: if a swipe
    /// changes the balance at the same moment, the save fails and we retry with fresh data.
    /// </summary>
    private Task<CardDto> ChangeLockAsync(int cardId, Action<CreditCard> change, CancellationToken ct) =>
        _unitOfWork.WithConcurrencyRetryAsync(async () =>
        {
            var card = await GetOwnCardAsync(cardId, ct);
            change(card);
            await _unitOfWork.SaveChangesAsync(ct);
            return card.ToDto();
        });

    private async Task<CreditCard> GetOwnCardAsync(int cardId, CancellationToken ct)
    {
        var card = await _cards.GetByIdAsync(cardId, ct);
        if (card is not null && _currentUser.IsAdmin)
            throw new ForbiddenException("Only the cardholder can change card controls. The bank uses Block / Unblock.");
        if (card is null || card.CardholderId != _currentUser.UserId)
            throw new NotFoundException($"Card {cardId} was not found.");
        return card;
    }

    private async Task<CardControlsDto> ToDtoAsync(CreditCard card, CancellationToken ct)
    {
        var controls = card.EnsureControls();
        var spent = await _transactions.GetApprovedSpendAsync(card.CardId, _rules.StartOfTodayUtc(), ct);

        ChannelControlDto For(TransactionChannel channel) =>
            new(controls.IsEnabled(channel), controls.DailyLimitFor(channel), spent.For(channel));

        return new CardControlsDto(
            card.CardId, card.MaskedCardNumber, card.CardStatus.ToString(), card.IsLocked, card.LockedAt,
            card.CreditLimit, _rules.Options.ContactlessPerTransactionLimit, _rules.Options.HomeCountryCode,
            For(TransactionChannel.Pos), For(TransactionChannel.Online), For(TransactionChannel.Contactless),
            For(TransactionChannel.Atm),
            new ChannelControlDto(controls.InternationalEnabled, controls.InternationalDailyLimit, spent.International),
            controls.UpdatedAt);
    }

    private static ChannelSetting ToSetting(ChannelSettingRequest r) => new(r.Enabled, r.DailyLimit);
}
