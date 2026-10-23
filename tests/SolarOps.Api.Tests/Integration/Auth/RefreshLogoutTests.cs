using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SolarOps.Api.Features.Auth;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public sealed class RefreshLogoutTests : IAsyncLifetime
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(JwtOptions.DefaultRefreshReuseGraceSeconds);

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly ClockedFactory _factory;

    public RefreshLogoutTests(PostgresFixture postgres) => _factory = new ClockedFactory(postgres, _clock);

    public Task InitializeAsync() => _factory.SeedIdentityAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<TokenResponse> ReadTokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static IEnumerable<string> RolesOf(string accessToken) =>
        new JsonWebToken(accessToken).Claims.Where(c => c.Type == "role").Select(c => c.Value);

    [Fact]
    public async Task Refresh_ReturnsNewTokenPair_StoresOnlyHash_AndRevokesOldToken()
    {
        using var client = _factory.CreateApiClient();
        var first = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        var second = await ReadTokensAsync(await AuthApi.RefreshAsync(client, first.RefreshToken));

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(15 * 60, second.ExpiresIn);
        Assert.Equal([Roles.Customer], RolesOf(second.AccessToken));

        var rows = await _factory.ExecuteDbAsync(db => db.Set<RefreshToken>().ToListAsync());
        var old = rows.Single(t => RefreshTokenCrypto.HashEquals(t.TokenHash, RefreshTokenCrypto.Hash(first.RefreshToken)));
        var current = rows.Single(t => RefreshTokenCrypto.HashEquals(t.TokenHash, RefreshTokenCrypto.Hash(second.RefreshToken)));
        Assert.Equal(RefreshTokenRevocationReason.Rotated, old.RevocationReason);
        Assert.Equal(current.Id, old.ReplacedById);
        Assert.Equal(old.FamilyId, current.FamilyId);
        Assert.Null(current.RevokedAt);
        Assert.Equal(_clock.GetUtcNow().AddDays(7), current.ExpiresAt);
    }

    [Fact]
    public async Task Refresh_ChainOfRotations_KeepsWorking()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(Grace + TimeSpan.FromSeconds(1));
            tokens = await ReadTokensAsync(await AuthApi.RefreshAsync(client, tokens.RefreshToken));
        }
    }

    [Fact]
    public async Task Refresh_UsedTokenAfterGraceWindow_Returns401AndRevokesWholeFamily()
    {
        using var client = _factory.CreateApiClient();
        var first = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());
        var second = await ReadTokensAsync(await AuthApi.RefreshAsync(client, first.RefreshToken));

        _clock.Advance(Grace + TimeSpan.FromSeconds(1));
        var replay = await AuthApi.RefreshAsync(client, first.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        // The holder of the newest token is cut off too (suspected theft).
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, second.RefreshToken)).StatusCode);
        var rows = await _factory.ExecuteDbAsync(db => db.Set<RefreshToken>().ToListAsync());
        Assert.All(rows, t => Assert.NotNull(t.RevokedAt));
        Assert.Contains(rows, t => t.RevocationReason == RefreshTokenRevocationReason.ReuseDetected);
    }

    [Fact]
    public async Task Refresh_SecondTabWithinGraceWindow_Succeeds_WithoutRevokingFamily()
    {
        using var client = _factory.CreateApiClient();
        var first = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());
        var tabA = await ReadTokensAsync(await AuthApi.RefreshAsync(client, first.RefreshToken));

        _clock.Advance(TimeSpan.FromSeconds(1));
        var tabB = await ReadTokensAsync(await AuthApi.RefreshAsync(client, first.RefreshToken));

        Assert.NotEqual(tabA.RefreshToken, tabB.RefreshToken);
        // Both tabs keep a working session.
        await ReadTokensAsync(await AuthApi.RefreshAsync(client, tabA.RefreshToken));
        await ReadTokensAsync(await AuthApi.RefreshAsync(client, tabB.RefreshToken));
    }

    [Fact]
    public async Task Refresh_ParallelRequestsWithSameToken_NoneFails()
    {
        using var client = _factory.CreateApiClient();
        var first = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AuthApi.RefreshAsync(client, first.RefreshToken)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var rows = await _factory.ExecuteDbAsync(db => db.Set<RefreshToken>().ToListAsync());
        // Exactly one request rotated the original token; the others got sibling tokens.
        Assert.Single(rows, t => t.RevocationReason == RefreshTokenRevocationReason.Rotated);
        Assert.Equal(5, rows.Count(t => t.RevokedAt is null));
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        _clock.Advance(TimeSpan.FromDays(7));

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, tokens.RefreshToken)).StatusCode);
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_UnknownToken_Returns401(string token)
    {
        using var client = _factory.CreateApiClient();

        var response = await AuthApi.RefreshAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_MissingToken_Returns400()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_LockedOutUser_Returns401()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, email);
        await _factory.ExecuteDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); // Identity uses the system clock
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, tokens.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ReflectsCurrentRoles()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, email);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.AddToRoleAsync((await users.FindByEmailAsync(email))!, Roles.Sales);
        }

        var refreshed = await ReadTokensAsync(await AuthApi.RefreshAsync(client, tokens.RefreshToken));

        Assert.Contains(Roles.Sales, RolesOf(refreshed.AccessToken));
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_SoRefreshFails()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        var logout = await AuthApi.LogoutAsync(client, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, tokens.RefreshToken)).StatusCode);
        var stored = await _factory.ExecuteDbAsync(db => db.Set<RefreshToken>().SingleAsync());
        Assert.Equal(RefreshTokenRevocationReason.Logout, stored.RevocationReason);
    }

    [Fact]
    public async Task Logout_RevokesWholeSession_IncludingRotatedAndSiblingTokens()
    {
        using var client = _factory.CreateApiClient();
        var first = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());
        var second = await ReadTokensAsync(await AuthApi.RefreshAsync(client, first.RefreshToken));
        var sibling = await ReadTokensAsync(await AuthApi.RefreshAsync(client, first.RefreshToken));

        await AuthApi.LogoutAsync(client, second.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, second.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, sibling.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_DoesNotAffectOtherSessionsOfSameUser()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        var phone = await AuthApi.RegisterAndLoginAsync(client, email);
        var laptop = await ReadTokensAsync(await AuthApi.LoginAsync(client, email));

        await AuthApi.LogoutAsync(client, phone.RefreshToken);

        await ReadTokensAsync(await AuthApi.RefreshAsync(client, laptop.RefreshToken));
    }

    [Fact]
    public async Task Logout_UnknownOrRepeatedToken_Returns204()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        Assert.Equal(HttpStatusCode.NoContent, (await AuthApi.LogoutAsync(client, "unknown-token")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AuthApi.LogoutAsync(client, tokens.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AuthApi.LogoutAsync(client, tokens.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_MissingToken_Returns400()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithExpiredOrInvalidAccessToken_StillWorks()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "expired.or.garbage");

        var response = await AuthApi.LogoutAsync(client, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class ClockedFactory(PostgresFixture postgres, TimeProvider clock) : JwtApiFactory(postgres)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }
}
