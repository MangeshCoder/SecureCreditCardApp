namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>Mapped to HTTP 403 by the API exception middleware (e.g. accessing someone else's card).</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
