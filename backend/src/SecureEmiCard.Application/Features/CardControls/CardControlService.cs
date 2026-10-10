using System.Globalization;
using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Application.Features.Otp;
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
/// Module 7: changes that make the card riskier (unlock, switching something on, raising a limit) need a
/// one-time code; every change sends the cardholder an alert.
/// </summary>
public class CardControlService : ICardControlService
{
    private readonly ICreditCardRepository _cards;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ICardControlRules _rules;
    private readonly IValidator<UpdateCardControlsRequest> _validator;
    private readonly IStepUpAuthenticator _stepUp;
    private readonly INotifier _notifier;

    public CardControlService(ICreditCardRepository cards, ITransactionRepository transactions, IUnitOfWork unitOfWork,
                              ICurrentUser currentUser, ICardControlRules rules, IValidator<UpdateCardControlsRequest> validator,
                              IStepUpAuthenticator stepUp, INotifier notifier)
    {
        _cards = cards;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _rules = rules;
        _validator = validator;
        _stepUp = stepUp;
        _notifier = notifier;
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

        var (pos, online, contactless, atm, international) = (ToSetting(request.Pos), ToSetting(request.Online),
            ToSetting(request.Contactless), ToSetting(request.Atm), ToSetting(request.International));
        CardControl.EnsureValid(pos, online, contactless, atm, international, card.CreditLimit); // before any OTP

        var controls = card.EnsureControls();
        if (controls.IsRiskIncrease(pos, online, contactless, atm, international))
        {
            // The code approves exactly THESE settings: if they are changed afterwards, it no longer fits.
            await _stepUp.RequireAsync(new StepUpRequest(card.CardholderId, OtpPurpose.CardControls,
                $"card={card.CardId}|{Fingerprint(request)}", $"to change the controls of {Alerts.CardName(card)}"), ct);
        }

        controls.Update(pos, online, contactless, atm, international, card.CreditLimit);
        await _notifier.AddAsync(card, Alerts.CardControlsChanged(card, CardControlSummary.Describe(controls)), ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await ToDtoAsync(card, ct);
    }

    /// <summary>Locking makes the card safer: no code needed.</summary>
    public Task<CardDto> LockAsync(int cardId, CancellationToken ct = default) =>
        ChangeLockAsync(cardId, card => card.Lock(), Alerts.CardLocked, ct);

    /// <summary>Unlocking makes it usable again - exactly what a thief would want, so it needs a code.</summary>
    public async Task<CardDto> UnlockAsync(int cardId, CancellationToken ct = default)
    {
        var card = await GetOwnCardAsync(cardId, ct);
        if (!card.IsLocked) throw new DomainException("Card is not locked."); // checked before any SMS is sent
        await _stepUp.RequireAsync(new StepUpRequest(card.CardholderId, OtpPurpose.UnlockCard,
            $"card={card.CardId}", $"to unlock {Alerts.CardName(card)}"), ct);

        return await ChangeLockAsync(cardId, c => c.Unlock(), Alerts.CardUnlocked, ct);
    }

    // ---- helpers -------------------------------------------------------------

    /// <summary>
    /// The lock is a column of CreditCards, whose AvailableBalance is a concurrency token: if a swipe
    /// changes the balance at the same moment, the save fails and we retry with fresh data.
    /// </summary>
    private Task<CardDto> ChangeLockAsync(int cardId, Action<CreditCard> change, Func<CreditCard, Alert> alert,
                                          CancellationToken ct) =>
        _unitOfWork.WithConcurrencyRetryAsync(async () =>
        {
            var card = await GetOwnCardAsync(cardId, ct);
            change(card);
            await _notifier.AddAsync(card, alert(card), ct);
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

    private static string Fingerprint(UpdateCardControlsRequest r)
    {
        static string S(ChannelSettingRequest s) =>
            $"{(s.Enabled ? 1 : 0)}:{s.DailyLimit?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"}";
        return $"pos={S(r.Pos)};online={S(r.Online)};contactless={S(r.Contactless)};atm={S(r.Atm)};international={S(r.International)}";
    }
}
