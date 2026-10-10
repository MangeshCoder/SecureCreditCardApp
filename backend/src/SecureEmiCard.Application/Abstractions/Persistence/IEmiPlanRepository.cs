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

    /// <summary>Module 8: plans whose "moved to EMI" is not on a statement yet (tracked), with the purchase loaded.</summary>
    Task<IReadOnlyList<EmiPlan>> GetUnbilledAsync(int cardId, CancellationToken ct = default);

    /// <summary>Module 8: plans moved to EMI on a statement, with the purchase loaded.</summary>
    Task<IReadOnlyList<EmiPlan>> GetByStatementAsync(int statementId, CancellationToken ct = default);

    /// <summary>Module 8: unpaid installments falling due on or before a date (shown on the statement).</summary>
    Task<decimal> GetInstallmentsDueByAsync(int cardId, DateOnly dueBy, CancellationToken ct = default);
}
