using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly AppDbContext _db;

    public TransactionRepository(AppDbContext db) => _db = db;

    public Task<CardTransaction?> GetByIdAsync(int transactionId, CancellationToken ct = default) =>
        _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);

    public async Task<(IReadOnlyList<CardTransaction> Items, int TotalCount)> GetPagedAsync(
        int? cardId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Transactions.AsNoTracking();
        if (cardId is not null) query = query.Where(t => t.CardId == cardId);

        var total = await query.CountAsync(ct);
        var items = await query.Include(t => t.Card)
                               .OrderByDescending(t => t.TransactionDate)
                               .ThenByDescending(t => t.TransactionId)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<CardTransaction>> GetConvertibleSwipesAsync(int cardId, DateTime sinceUtc, CancellationToken ct = default) =>
        await _db.Transactions.AsNoTracking()
                 .Where(t => t.CardId == cardId
                             && t.TransactionType == TransactionType.Swipe
                             && t.TransactionStatus == TransactionStatus.Completed
                             && !t.IsEmiConverted
                             && t.TransactionDate >= sinceUtc)
                 .OrderByDescending(t => t.TransactionDate)
                 .ToListAsync(ct);

    public async Task<DailySpend> GetApprovedSpendAsync(int cardId, DateTime sinceUtc, CancellationToken ct = default)
    {
        // One small GROUP BY in SQL (uses IX_Transactions_CardId_Date); at most 8 rows come back.
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.CardId == cardId
                        && t.TransactionType == TransactionType.Swipe
                        && t.TransactionStatus != TransactionStatus.Declined
                        && t.TransactionDate >= sinceUtc
                        && t.Channel != null)
            .GroupBy(t => new { t.Channel, t.IsInternational })
            .Select(g => new { g.Key.Channel, g.Key.IsInternational, Amount = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        return new DailySpend(
            rows.GroupBy(r => r.Channel!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount)),
            rows.Where(r => r.IsInternational).Sum(r => r.Amount));
    }

    public async Task AddAsync(CardTransaction transaction, CancellationToken ct = default) =>
        await _db.Transactions.AddAsync(transaction, ct);
}
