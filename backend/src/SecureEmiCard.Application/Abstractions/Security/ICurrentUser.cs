namespace SecureEmiCard.Application.Abstractions.Security;

/// <summary>The authenticated caller, resolved from the JWT by the API layer.</summary>
public interface ICurrentUser
{
    int UserId { get; }
    bool IsAdmin { get; }
}
