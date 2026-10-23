using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Auth;

/// <summary>Creates and stores refresh tokens (only their SHA-256 hash is persisted).</summary>
public interface IRefreshTokenService
{
    /// <summary>Creates a refresh token for a new session (new family) and returns the plain token.</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new one of the same session (single use). Returns null when the token is
    /// unknown, expired or revoked. Presenting an already rotated token again revokes the whole session (suspected
    /// theft), unless it happens within the reuse grace window (concurrent refresh from two tabs): then the caller
    /// gets another valid token and nothing is revoked.
    /// </summary>
    Task<RotatedRefreshToken?> RotateAsync(string token, CancellationToken cancellationToken);

    /// <summary>Revokes every token of the session the given token belongs to. Unknown tokens are ignored.</summary>
    Task RevokeSessionAsync(string token, RefreshTokenRevocationReason reason, CancellationToken cancellationToken);
}

/// <summary>Result of a successful rotation.</summary>
public sealed record RotatedRefreshToken(Guid UserId, IssuedRefreshToken Token);

/// <summary>Plain refresh token (returned to the client once) and its expiry.</summary>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

internal sealed class RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider timeProvider, ILogger<RefreshTokenService> logger)
    : IRefreshTokenService
{
    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (issued, entity) = Create(userId, Guid.CreateVersion7());
        db.Set<RefreshToken>().Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return issued;
    }

    public async Task<RotatedRefreshToken?> RotateAsync(string token, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenCrypto.Hash(token);
        var tokens = db.Set<RefreshToken>();
        var now = timeProvider.GetUtcNow();

        var stored = await tokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null || stored.IsExpired(now))
        {
            return null;
        }

        if (stored.RevokedAt is null)
        {
            // Atomic claim: only one concurrent request flips RevokedAt from null, the others see 0 rows.
            var (issued, successor) = Create(stored.UserId, stored.FamilyId);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await tokens
                .Where(t => t.Id == stored.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevocationReason, RefreshTokenRevocationReason.Rotated)
                    .SetProperty(t => t.ReplacedById, successor.Id), cancellationToken);
            if (claimed == 1)
            {
                tokens.Add(successor);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new RotatedRefreshToken(stored.UserId, issued);
            }

            await transaction.RollbackAsync(cancellationToken);
            stored = await tokens.AsNoTracking().SingleAsync(t => t.Id == stored.Id, cancellationToken);
        }

        // The token was already used (or revoked) by someone else.
        if (stored.RevocationReason == RefreshTokenRevocationReason.Rotated
            && stored.RevokedAt is { } rotatedAt
            && now - rotatedAt <= options.Value.RefreshReuseGrace
            && !await IsFamilyRevokedAsync(stored.FamilyId, cancellationToken))
        {
            var (issued, sibling) = Create(stored.UserId, stored.FamilyId);
            tokens.Add(sibling);
            await db.SaveChangesAsync(cancellationToken);
            return new RotatedRefreshToken(stored.UserId, issued);
        }

        if (stored.RevocationReason == RefreshTokenRevocationReason.Rotated)
        {
            await RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.ReuseDetected, now, cancellationToken);
            logger.LogWarning("Refresh token reuse detected for user {UserId}; session {FamilyId} revoked", stored.UserId, stored.FamilyId);
        }

        return null;
    }

    public async Task RevokeSessionAsync(string token, RefreshTokenRevocationReason reason, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenCrypto.Hash(token);
        var familyId = await db.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (familyId is { } id)
        {
            await RevokeFamilyAsync(id, reason, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    // Rotated tokens keep their own reason, so a revoked session is recognised by any non-rotation reason in the family.
    private Task<bool> IsFamilyRevokedAsync(Guid familyId, CancellationToken cancellationToken) =>
        db.Set<RefreshToken>().AsNoTracking().AnyAsync(
            t => t.FamilyId == familyId && t.RevocationReason != null && t.RevocationReason != RefreshTokenRevocationReason.Rotated,
            cancellationToken);

    private Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<RefreshToken>()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevocationReason, reason), cancellationToken);

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
