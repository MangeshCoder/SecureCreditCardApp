using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class CardStatementRepository : ICardStatementRepository
{
    private readonly AppDbContext _db;

    public CardStatementRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CardStatement statement, CancellationToken ct = default) =>
        await _db.CardStatements.AddAsync(statement, ct);

    public Task<CardStatement?> GetByIdAsync(int statementId, CancellationToken ct = default) =>
        _db.CardStatements.Include(s => s.Card).ThenInclude(c => c!.Cardholder)
           .FirstOrDefaultAsync(s => s.StatementId == statementId, ct);

    public Task<CardStatement?> GetLatestAsync(int cardId, CancellationToken ct = default) =>
        _db.CardStatements.Where(s => s.CardId == cardId)
           .OrderByDescending(s => s.PeriodEnd).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CardStatement>> GetByCardAsync(int cardId, CancellationToken ct = default) =>
        await _db.CardStatements.AsNoTracking().Where(s => s.CardId == cardId)
                 .OrderByDescending(s => s.PeriodEnd).ToListAsync(ct);

    public async Task<IReadOnlyList<CardStatement>> GetUnassessedPastDueAsync(int? cardId, DateTime nowUtc, CancellationToken ct = default)
    {
        var query = _db.CardStatements.Where(s => s.AssessedAt == null && s.DueDate < nowUtc);
        if (cardId is not null) query = query.Where(s => s.CardId == cardId);
        return await query.OrderBy(s => s.DueDate).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CardStatement>> GetReminderCandidatesAsync(DateTime nowUtc, DateTime dueBeforeUtc, CancellationToken ct = default) =>
        await _db.CardStatements
                 .Where(s => s.Status == StatementStatus.Open && s.ReminderSentAt == null
                             && s.DueDate > nowUtc && s.DueDate <= dueBeforeUtc)
                 .OrderBy(s => s.DueDate).ToListAsync(ct);
}
