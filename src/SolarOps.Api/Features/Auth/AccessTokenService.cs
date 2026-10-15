using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SolarOps.Api.Infrastructure.Identity;

namespace SolarOps.Api.Features.Auth;

/// <summary>Issues signed JWT access tokens.</summary>
public interface IAccessTokenService
{
    AccessToken Create(AppUser user, IEnumerable<string> roles);
}

/// <summary>Encoded access token and its lifetime.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

internal sealed class AccessTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : IAccessTokenService
{
    /// <summary>Claim type of roles in the token (mapped to <see cref="ClaimTypes.Role"/> on validation).</summary>
    public const string RoleClaim = "role";

    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(AppUser user, IEnumerable<string> roles)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.Add(jwt.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        claims.AddRange(roles.Distinct(StringComparer.Ordinal).Select(r => new Claim(RoleClaim, r)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(jwt), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires, (int)jwt.AccessTokenLifetime.TotalSeconds);
    }

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions jwt) => new(Encoding.UTF8.GetBytes(jwt.SigningKey));
}
