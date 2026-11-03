using SolarOps.Api.Features.Auth;

namespace SolarOps.Api.Tests.Unit.Auth;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private static RefreshToken Token() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), new byte[32], Now, Now.AddDays(7));

    [Fact]
    public void NewToken_IsActive_AndNotRevoked()
    {
        var token = Token();

        Assert.True(token.IsActive(Now));
        Assert.Null(token.RevokedAt);
        Assert.Null(token.RevocationReason);
        Assert.NotEqual(Guid.Empty, token.Id);
    }

    [Fact]
    public void IsExpired_FalseJustBeforeExpiry_TrueFromExpiryOnwards()
    {
        var token = Token();

        Assert.False(token.IsExpired(Now.AddDays(7).AddTicks(-1)));
        Assert.True(token.IsExpired(Now.AddDays(7)));
        Assert.True(token.IsExpired(Now.AddDays(8)));
    }

    [Fact]
    public void IsActive_FalseAfterExpiry()
    {
        var token = Token();

        Assert.True(token.IsActive(Now.AddDays(7).AddTicks(-1)));
        Assert.False(token.IsActive(Now.AddDays(7)));
    }

    [Fact]
    public void Constructor_KeepsSessionFamilyAndHash()
    {
        var userId = Guid.CreateVersion7();
        var familyId = Guid.CreateVersion7();
        var hash = new byte[] { 1, 2, 3 };

        var token = new RefreshToken(userId, familyId, hash, Now, Now.AddDays(1));

        Assert.Equal(userId, token.UserId);
        Assert.Equal(familyId, token.FamilyId);
        Assert.Equal(hash, token.TokenHash);
        Assert.Equal(Now, token.CreatedAt);
    }
}
