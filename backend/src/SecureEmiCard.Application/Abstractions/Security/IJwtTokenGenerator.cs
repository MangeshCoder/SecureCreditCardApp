using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Security;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(Cardholder user);
}
