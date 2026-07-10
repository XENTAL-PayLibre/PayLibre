using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Schools;

namespace PayLibre.Infrastructure.Security;

/// <summary>Issues signed dashboard access tokens (HS256). Carries tenant + role for authorization.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public const string DashboardScope = "dashboard";
    private readonly JwtOptions _options = options.Value;

    public AccessToken IssueAccessToken(SchoolUser user)
    {
        var expiresAt = clock.UtcNow.AddSeconds(_options.AccessTokenLifetimeSeconds);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant_id", user.SchoolId.ToString()),
            new("scope", DashboardScope),
            new("role", user.Role.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: clock.UtcNow.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
