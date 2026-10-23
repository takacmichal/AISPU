using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Auth;

/// <summary>Creates and stores refresh tokens (only their SHA-256 hash is persisted).</summary>
public interface IRefreshTokenService
{
    /// <summary>Creates a refresh token for a new session (new family) and returns the plain token.</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Plain refresh token (returned to the client once) and its expiry.</summary>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

internal sealed class RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider timeProvider)
    : IRefreshTokenService
{
    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (issued, entity) = Create(userId, Guid.CreateVersion7());
        db.Set<RefreshToken>().Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return issued;
    }

    private (IssuedRefreshToken Issued, RefreshToken Entity) Create(Guid userId, Guid familyId)
    {
        var now = timeProvider.GetUtcNow();
        var token = RefreshTokenCrypto.Generate();
        var expiresAt = now.Add(options.Value.RefreshTokenLifetime);
        return (new IssuedRefreshToken(token, expiresAt),
            new RefreshToken(userId, familyId, RefreshTokenCrypto.Hash(token), now, expiresAt));
    }
}

/// <summary>Generation and hashing of opaque refresh tokens.</summary>
public static class RefreshTokenCrypto
{
    public const int TokenBytes = 32;

    /// <summary>256 bits from a CSPRNG, base64url encoded (43 characters).</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>SHA-256 of the token; a fast hash is fine because the token itself has 256 bits of entropy.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Constant-time comparison of two hashes.</summary>
    public static bool HashEquals(byte[] left, byte[] right) => CryptographicOperations.FixedTimeEquals(left, right);
}
