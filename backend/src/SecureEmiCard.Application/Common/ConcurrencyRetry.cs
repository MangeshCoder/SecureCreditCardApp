using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Common.Exceptions;

namespace SecureEmiCard.Application.Common;

public static class ConcurrencyRetry
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// Runs an operation and, if another request changed the same rows in the meantime
    /// (optimistic concurrency), discards the stale data and runs it again - up to 3 times.
    /// The operation must re-read everything it needs, because the change tracker is cleared.
    /// </summary>
    public static async Task<T> WithConcurrencyRetryAsync<T>(this IUnitOfWork unitOfWork, Func<Task<T>> operation)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                unitOfWork.ClearChanges();
            }
        }
    }
}
