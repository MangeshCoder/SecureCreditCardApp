using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Security;

/// <summary>Issues JWT access tokens signed with HMAC-SHA512 (as required by the specification).</summary>
public class JwtTokenGenerator : IJwtTokenGenerator
{
    /// <summary>Short claim name used for the role; the API is configured with RoleClaimType = "role".</summary>
    public const string RoleClaimType = "role";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(Cardholder user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.CardholderId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.FullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(RoleClaimType, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: _credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
