using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;

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

    public async Task AddAsync(CardTransaction transaction, CancellationToken ct = default) =>
        await _db.Transactions.AddAsync(transaction, ct);
}
