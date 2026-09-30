namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>
/// The row was changed by another request between our read and our write (optimistic concurrency).
/// Services retry automatically; if retries run out the API returns HTTP 409.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null) : base(message, inner) { }
}
