using System.IdentityModel.Tokens.Jwt;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Api.Infrastructure;

/// <summary>Reads the authenticated user's id and role from the validated JWT of the current request.</summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public int UserId
    {
        get
        {
            var sub = _accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return int.TryParse(sub, out var id)
                ? id
                : throw new UnauthorizedAccessException("No authenticated user.");
        }
    }

    public bool IsAdmin => _accessor.HttpContext?.User.IsInRole(nameof(UserRole.Admin)) ?? false;
}
