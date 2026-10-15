using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using SolarOps.Api.Features.Auth;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public sealed class RegisterLoginTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JwtApiFactory _factory = new(postgres);

    public Task InitializeAsync() => _factory.SeedIdentityAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Register_CreatesCustomer_WithHashedPassword()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();

        var response = await AuthApi.RegisterAsync(client, email);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Equal([Roles.Customer], user.Roles);

        var stored = await _factory.ExecuteDbAsync(db => db.Users.SingleAsync(u => u.Id == user.Id));
        Assert.False(string.IsNullOrEmpty(stored.PasswordHash));
        Assert.DoesNotContain(AuthApi.ValidPassword, stored.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            new PasswordHasher<AppUser>().VerifyHashedPassword(stored, stored.PasswordHash!, AuthApi.ValidPassword));
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("alllowercase123")]
    [InlineData("NoDigitsInHere")]
    public async Task Register_WeakPassword_Returns400(string password)
    {
        using var client = _factory.CreateApiClient();

        var response = await AuthApi.RegisterAsync(client, AuthApi.UniqueEmail(), password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(nameof(RegisterRequest.Password), problem!.Errors.Keys);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        using var client = _factory.CreateApiClient();

        var response = await AuthApi.RegisterAsync(client, "not-an-email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAsync(client, email);

        var response = await AuthApi.RegisterAsync(client, email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsValidJwtWithRolesAndRefreshToken()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();

        var tokens = await AuthApi.RegisterAndLoginAsync(client, email);

        Assert.Equal(15 * 60, tokens.ExpiresIn);
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        // Validate with exactly the parameters the JWT Bearer handler uses.
        var bearer = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.AccessToken, bearer.TokenValidationParameters);
        Assert.True(result.IsValid, result.Exception?.Message);

        var jwt = new JsonWebToken(tokens.AccessToken);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(email, jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal([Roles.Customer], jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value));
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
        Assert.True(Guid.TryParse(jwt.Subject, out _));
    }

    [Fact]
    public async Task Login_StoresOnlyHashOfRefreshToken()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());

        var stored = await _factory.ExecuteDbAsync(db => db.Set<RefreshToken>().SingleAsync());

        Assert.Equal(RefreshTokenCrypto.Hash(tokens.RefreshToken), stored.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), stored.ExpiresAt - stored.CreatedAt);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_Return401WithSameBody()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAsync(client, email);

        var wrongPassword = await AuthApi.LoginAsync(client, email, "Wrong-Password-1");
        var unknownEmail = await AuthApi.LoginAsync(client, AuthApi.UniqueEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var first = await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>();
        var second = await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(first!.Detail, second!.Detail);
        Assert.Equal(first.Title, second.Title);
    }

    [Fact]
    public async Task Login_AfterFiveFailures_LocksOutEvenWithCorrectPassword()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAsync(client, email);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.LoginAsync(client, email, "Wrong-Password-1")).StatusCode);
        }

        var response = await AuthApi.LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var lockoutEnd = await _factory.ExecuteDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.LockoutEnd).SingleAsync());
        Assert.NotNull(lockoutEnd);
    }

    [Fact]
    public async Task Login_MissingFields_Returns400()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Seeder_CreatesAllRolesIdempotently()
    {
        await _factory.SeedIdentityAsync();

        var roles = await _factory.ExecuteDbAsync(db => db.Roles.Select(r => r.Name!).ToListAsync());

        Assert.Equal(Roles.All.Order(), roles.Order());
    }

    [Fact]
    public async Task JwtClaims_AreMappedForCurrentUser()
    {
        using var client = _factory.CreateApiClient();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, AuthApi.UniqueEmail());
        var bearer = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        var result = await bearer.TokenHandlers.Single().ValidateTokenAsync(tokens.AccessToken, bearer.TokenValidationParameters);

        Assert.True(result.IsValid);
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        Assert.NotNull(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.IsInRole(Roles.Customer));
    }
}
