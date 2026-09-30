namespace SecureEmiCard.Application.Abstractions.Persistence;

/// <summary>Commits all changes tracked by the repositories in a single database transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
