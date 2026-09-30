namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>Mapped to HTTP 404 by the API exception middleware.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
