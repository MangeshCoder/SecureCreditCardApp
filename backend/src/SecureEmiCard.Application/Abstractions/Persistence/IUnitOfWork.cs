namespace SecureEmiCard.Application.Abstractions.Persistence;

/// <summary>Commits all changes tracked by the repositories in a single database transaction.</summary>
public interface IUnitOfWork
{
    /// <exception cref="Common.Exceptions.ConcurrencyConflictException">
    /// A row being updated was modified by someone else since it was read.
    /// </exception>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Forgets all loaded/pending entities, so a retried operation re-reads fresh data.</summary>
    void ClearChanges();
}
