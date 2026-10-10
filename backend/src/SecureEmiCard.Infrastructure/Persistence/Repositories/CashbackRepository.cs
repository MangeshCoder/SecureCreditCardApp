using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class CashbackRepository : ICashbackRepository
{
    private readonly AppDbContext _db;

    public CashbackRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CashbackLog log, CancellationToken ct = default) =>
        await _db.CashbackLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<CashbackLog>> GetByTransactionIdsAsync(IReadOnlyCollection<int> transactionIds, CancellationToken ct = default)
    {
        if (transactionIds.Count == 0) return Array.Empty<CashbackLog>();
        return await _db.CashbackLogs.AsNoTracking()
                        .Where(c => transactionIds.Contains(c.TransactionId))
                        .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<CashbackLog> Items, int TotalCount)> GetPagedByCardAsync(
        int cardId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.CashbackLogs.AsNoTracking().Where(c => c.CardId == cardId);
        var total = await query.CountAsync(ct);
        var items = await query.Include(c => c.Transaction)
                               .OrderByDescending(c => c.CreditedDate)
                               .ThenByDescending(c => c.CashbackId)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>
    /// All figures are aggregated by SQL Server (SUM / GROUP BY) - we never load every row into memory.
    /// </summary>
    public async Task<CashbackTotals> GetTotalsAsync(int cardId, DateTime monthStartUtc, CancellationToken ct = default)
    {
        var logs = _db.CashbackLogs.AsNoTracking().Where(c => c.CardId == cardId);

        var earned = await logs.Where(c => c.CashbackType == CashbackType.Earned).SumAsync(c => c.CashbackAmount, ct);
        var reversed = await logs.Where(c => c.CashbackType == CashbackType.Reversed).SumAsync(c => c.CashbackAmount, ct);
        var thisMonth = await logs.Where(c => c.CreditedDate >= monthStartUtc).SumAsync(c => c.CashbackAmount, ct);

        var byCategory = await logs
            .Join(_db.Transactions, c => c.TransactionId, t => t.TransactionId,
                  (c, t) => new { t.MerchantCategoryCode, c.CashbackAmount, c.CashbackType })
            .GroupBy(x => x.MerchantCategoryCode)
            .Select(g => new
            {
                Mcc = g.Key,
                Net = g.Sum(x => x.CashbackAmount),
                Count = g.Count(x => x.CashbackType == CashbackType.Earned)
            })
            .ToListAsync(ct);

        return new CashbackTotals(earned, reversed, thisMonth,
            byCategory.Select(x => (x.Mcc, x.Net, x.Count)).ToList());
    }

    public async Task<IReadOnlyList<CashbackLog>> GetUnbilledAsync(int cardId, CancellationToken ct = default) =>
        await _db.CashbackLogs.Include(c => c.Transaction)
                 .Where(c => c.CardId == cardId && c.StatementId == null)
                 .OrderBy(c => c.CashbackId)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<CashbackLog>> GetByStatementAsync(int statementId, CancellationToken ct = default) =>
        await _db.CashbackLogs.AsNoTracking().Include(c => c.Transaction)
                 .Where(c => c.StatementId == statementId)
                 .OrderBy(c => c.CashbackId)
                 .ToListAsync(ct);
}
