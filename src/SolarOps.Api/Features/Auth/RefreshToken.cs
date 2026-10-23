using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SolarOps.Api.Infrastructure.Identity;

namespace SolarOps.Api.Features.Auth;

/// <summary>Why a refresh token stopped being usable.</summary>
public enum RefreshTokenRevocationReason
{
    Rotated,
    Logout,
    ReuseDetected,
    PasswordReset,
    UserDisabled,
}

/// <summary>
/// Refresh token of one session (spike #218). Only the SHA-256 hash of the token is stored.
/// Tokens issued by rotation share the <see cref="FamilyId"/> of the login that started the session.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid userId, Guid familyId, byte[] tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenRevocationReason? RevocationReason { get; private set; }

    /// <summary>Token that replaced this one during rotation.</summary>
    public Guid? ReplacedById { get; private set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && !IsExpired(now);
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(32);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId);
        builder.Property(t => t.RevocationReason).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
