using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface IEmiPlanRepository
{
    Task AddAsync(EmiPlan plan, CancellationToken ct = default);

    /// <summary>Tracked (for updates), with schedule and purchase loaded.</summary>
    Task<EmiPlan?> GetByIdAsync(int emiPlanId, CancellationToken ct = default);

    /// <summary>Read-only, newest first, with schedule and purchase loaded.</summary>
    Task<IReadOnlyList<EmiPlan>> GetByCardAsync(int cardId, CancellationToken ct = default);

    /// <summary>Principal of all unpaid installments on the card (the part of the limit locked in EMIs).</summary>
    Task<decimal> GetOutstandingPrincipalAsync(int cardId, CancellationToken ct = default);
}
