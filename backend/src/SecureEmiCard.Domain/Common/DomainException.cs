namespace SecureEmiCard.Domain.Common;

/// <summary>
/// Thrown when a business rule of the domain is violated
/// (e.g. blocking an already blocked card). The API maps it to HTTP 400/409.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
