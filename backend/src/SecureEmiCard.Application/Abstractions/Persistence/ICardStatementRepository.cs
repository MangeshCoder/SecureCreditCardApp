using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface ICardStatementRepository
{
    Task AddAsync(CardStatement statement, CancellationToken ct = default);

    /// <summary>With the card and its cardholder (for the statement page and the PDF).</summary>
    Task<CardStatement?> GetByIdAsync(int statementId, CancellationToken ct = default);

    Task<CardStatement?> GetLatestAsync(int cardId, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<CardStatement>> GetByCardAsync(int cardId, CancellationToken ct = default);

    /// <summary>Statements whose due date has passed but whose outcome (late fee, interest) is not decided yet.
    /// cardId = null: all cards (the scheduler).</summary>
    Task<IReadOnlyList<CardStatement>> GetUnassessedPastDueAsync(int? cardId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Open statements due within the window that have not had a reminder yet.</summary>
    Task<IReadOnlyList<CardStatement>> GetReminderCandidatesAsync(DateTime nowUtc, DateTime dueBeforeUtc, CancellationToken ct = default);
}
