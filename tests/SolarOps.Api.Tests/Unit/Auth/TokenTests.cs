using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using SolarOps.Api.Features.Auth;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Validation;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Unit.Auth;

public sealed class TokenTests
{
    private static JwtOptions Options() => new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningKey = ApiFactory.TestJwtSigningKey,
    };

    [Fact]
    public void AccessToken_ContainsSubjectEmailRolesAndExpiresIn15Minutes()
    {
        var now = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        var service = new AccessTokenService(Microsoft.Extensions.Options.Options.Create(Options()), new FixedTimeProvider(now));
        var user = new AppUser { Email = "a@example.com", UserName = "a@example.com" };

        var token = service.Create(user, [Roles.Sales, Roles.Designer, Roles.Sales]);

        var jwt = new JsonWebToken(token.Token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("a@example.com", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal([Roles.Sales, Roles.Designer], jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value));
        Assert.Equal(now.UtcDateTime, jwt.IssuedAt);
        Assert.Equal(now.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(900, token.ExpiresInSeconds);
        Assert.Equal("issuer", jwt.Issuer);
        Assert.Equal(["audience"], jwt.Audiences);
    }

    [Fact]
    public void RefreshToken_IsRandom256BitBase64Url()
    {
        var first = RefreshTokenCrypto.Generate();
        var second = RefreshTokenCrypto.Generate();

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
    }

    [Fact]
    public void RefreshTokenHash_IsSha256AndComparedInConstantTime()
    {
        var token = RefreshTokenCrypto.Generate();

        var hash = RefreshTokenCrypto.Hash(token);

        Assert.Equal(32, hash.Length);
        Assert.True(RefreshTokenCrypto.HashEquals(hash, RefreshTokenCrypto.Hash(token)));
        Assert.False(RefreshTokenCrypto.HashEquals(hash, RefreshTokenCrypto.Hash(token + "x")));
    }

    [Fact]
    public void JwtOptions_Valid()
    {
        Assert.Empty(DataAnnotationsValidator.Validate(Options()));
    }

    [Fact]
    public void JwtOptions_RejectShortSigningKey()
    {
        var options = Options();
        options.SigningKey = "too-short";

        Assert.Contains(nameof(JwtOptions.SigningKey), DataAnnotationsValidator.Validate(options).Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void JwtOptions_AccessTokenLifetimeIsAtMost15Minutes(int minutes)
    {
        var options = Options();
        options.AccessTokenMinutes = minutes;

        Assert.Contains(nameof(JwtOptions.AccessTokenMinutes), DataAnnotationsValidator.Validate(options).Keys);
    }

    [Fact]
    public void RefreshToken_IsActiveUntilExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), [1], now, now.AddDays(7));

        Assert.True(token.IsActive(now));
        Assert.True(token.IsActive(now.AddDays(7).AddTicks(-1)));
        Assert.False(token.IsActive(now.AddDays(7)));
    }
}
