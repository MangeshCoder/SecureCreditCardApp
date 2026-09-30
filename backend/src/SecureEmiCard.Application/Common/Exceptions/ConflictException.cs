namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>Mapped to HTTP 409 by the API exception middleware (e.g. duplicate email).</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
