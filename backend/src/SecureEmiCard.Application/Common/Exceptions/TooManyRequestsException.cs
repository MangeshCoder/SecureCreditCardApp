namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>Mapped to HTTP 429, e.g. too many one-time codes requested in a short time.</summary>
public class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message) : base(message) { }
}
