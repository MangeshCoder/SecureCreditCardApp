using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class EmiPlanRepository : IEmiPlanRepository
{
    private readonly AppDbContext _db;

    public EmiPlanRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(EmiPlan plan, CancellationToken ct = default) =>
        await _db.EmiPlans.AddAsync(plan, ct);

    public Task<EmiPlan?> GetByIdAsync(int emiPlanId, CancellationToken ct = default) =>
        _db.EmiPlans.Include(p => p.Schedules)
                    .Include(p => p.Transaction)
                    .FirstOrDefaultAsync(p => p.EmiPlanId == emiPlanId, ct);

    public async Task<IReadOnlyList<EmiPlan>> GetByCardAsync(int cardId, CancellationToken ct = default) =>
        await _db.EmiPlans.AsNoTracking()
                 .Include(p => p.Schedules)
                 .Include(p => p.Transaction)
                 .Where(p => p.CardId == cardId)
                 .OrderByDescending(p => p.CreatedDate)
                 .ToListAsync(ct);

    /// <summary>One SUM over the unpaid installments of the card's plans - computed by the database.</summary>
    public async Task<IReadOnlyList<EmiPlan>> GetUnbilledAsync(int cardId, CancellationToken ct = default) =>
        await _db.EmiPlans.Include(p => p.Transaction)
                 .Where(p => p.CardId == cardId && p.StatementId == null)
                 .OrderBy(p => p.EmiPlanId)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<EmiPlan>> GetByStatementAsync(int statementId, CancellationToken ct = default) =>
        await _db.EmiPlans.AsNoTracking().Include(p => p.Transaction)
                 .Where(p => p.StatementId == statementId)
                 .OrderBy(p => p.EmiPlanId)
                 .ToListAsync(ct);

    public Task<decimal> GetInstallmentsDueByAsync(int cardId, DateOnly dueBy, CancellationToken ct = default) =>
        _db.EmiSchedules
           .Where(s => s.PaymentStatus == InstallmentStatus.Pending && s.DueDate <= dueBy &&
                       _db.EmiPlans.Any(p => p.EmiPlanId == s.EmiPlanId && p.CardId == cardId))
           .SumAsync(s => s.AmountDue, ct);

    public Task<decimal> GetOutstandingPrincipalAsync(int cardId, CancellationToken ct = default) =>
        _db.EmiSchedules
           .Where(s => s.PaymentStatus == InstallmentStatus.Pending &&
                       _db.EmiPlans.Any(p => p.EmiPlanId == s.EmiPlanId && p.CardId == cardId))
           .SumAsync(s => s.PrincipalComponent, ct);
}
