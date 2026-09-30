namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>Mapped to HTTP 401 by the API exception middleware (e.g. bad credentials or wrong PIN).</summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}
