using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Cardholders;

public record CardholderDto(int CardholderId, string FirstName, string LastName, string Email,
                            string PhoneNumber, string Role, bool IsActive, DateTime CreatedAt, int CardCount);

public interface ICardholderService
{
    Task<IReadOnlyList<CardholderDto>> GetAllAsync(CancellationToken ct = default);
    Task<CardholderDto> GetByIdAsync(int cardholderId, CancellationToken ct = default);
    Task SetActiveAsync(int cardholderId, bool isActive, CancellationToken ct = default);
}

/// <summary>Admin-only cardholder management (onboarding review, activation/deactivation).</summary>
public class CardholderService : ICardholderService
{
    private readonly ICardholderRepository _cardholders;
    private readonly IUnitOfWork _unitOfWork;

    public CardholderService(ICardholderRepository cardholders, IUnitOfWork unitOfWork)
    {
        _cardholders = cardholders;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<CardholderDto>> GetAllAsync(CancellationToken ct = default)
        => (await _cardholders.GetAllAsync(ct)).Select(ToDto).ToList();

    public async Task<CardholderDto> GetByIdAsync(int cardholderId, CancellationToken ct = default)
        => ToDto(await GetOrThrowAsync(cardholderId, ct));

    public async Task SetActiveAsync(int cardholderId, bool isActive, CancellationToken ct = default)
    {
        var cardholder = await GetOrThrowAsync(cardholderId, ct);
        if (isActive) cardholder.Activate(); else cardholder.Deactivate();
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Cardholder> GetOrThrowAsync(int cardholderId, CancellationToken ct)
        => await _cardholders.GetByIdAsync(cardholderId, ct)
           ?? throw new NotFoundException($"Cardholder {cardholderId} was not found.");

    private static CardholderDto ToDto(Cardholder c) =>
        new(c.CardholderId, c.FirstName, c.LastName, c.Email, c.PhoneNumber,
            c.Role.ToString(), c.IsActive, c.CreatedAt, c.Cards.Count);
}
