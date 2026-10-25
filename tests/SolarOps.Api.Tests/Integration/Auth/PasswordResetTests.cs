using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SolarOps.Api.Features.Auth;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public sealed class PasswordResetTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New-Pass-2027";

    private readonly CapturingSender _sender = new();
    private readonly ResetFactory _factory;

    public PasswordResetTests(PostgresFixture postgres) => _factory = new ResetFactory(postgres, _sender, null);

    public Task InitializeAsync() => _factory.SeedIdentityAsync();

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        // Each factory owns a database and its own pool; release idle connections so the server limit is not hit.
        NpgsqlConnection.ClearAllPools();
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string token, string newPassword = NewPassword) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new { email, token, newPassword });

    private static string TokenFrom(string link) =>
        HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;

    [Fact]
    public async Task Forgot_SendsLinkWithToken_ToExistingAccount()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAndLoginAsync(client, email);

        using var response = await ForgotAsync(client, email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var (to, link) = Assert.Single(_sender.Sent);
        Assert.Equal(email, to);
        Assert.StartsWith("http://localhost:5173/reset-password?", link, StringComparison.Ordinal);
        Assert.Equal(email, HttpUtility.ParseQueryString(new Uri(link).Query)["email"]);
        Assert.False(string.IsNullOrEmpty(TokenFrom(link)));
    }

    [Fact]
    public async Task Forgot_UnknownEmail_Returns202LikeKnownEmail_AndSendsNothing()
    {
        using var client = _factory.CreateApiClient();
        var known = AuthApi.UniqueEmail();
        await AuthApi.RegisterAndLoginAsync(client, known);

        using var knownResponse = await ForgotAsync(client, known);
        using var unknownResponse = await ForgotAsync(client, AuthApi.UniqueEmail());

        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        Assert.Equal(await knownResponse.Content.ReadAsStringAsync(), await unknownResponse.Content.ReadAsStringAsync());
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Reset_ChangesPassword_RevokesSessions_AndTokenIsSingleUse()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        var tokens = await AuthApi.RegisterAndLoginAsync(client, email);
        await ForgotAsync(client, email);
        var token = TokenFrom(_sender.Sent.Single().Link);

        using var reset = await ResetAsync(client, email, token);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthApi.LoginAsync(client, email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.LoginAsync(client, email)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.RefreshAsync(client, tokens.RefreshToken)).StatusCode);
        var rows = await _factory.ExecuteDbAsync(db => db.Set<RefreshToken>().ToListAsync());
        Assert.Contains(rows, t => t.RevocationReason == RefreshTokenRevocationReason.PasswordReset);

        using var reuse = await ResetAsync(client, email, token, "Another-Pass-2028");
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task Reset_WeakPassword_Returns400OnNewPassword_AndTokenStaysUsable()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAndLoginAsync(client, email);
        await ForgotAsync(client, email);
        var token = TokenFrom(_sender.Sent.Single().Link);

        using var weak = await ResetAsync(client, email, token, "short");

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var problem = await weak.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("NewPassword", problem!.Errors.Keys);
        Assert.Equal(HttpStatusCode.OK, (await AuthApi.LoginAsync(client, email)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, token)).StatusCode);
    }

    [Fact]
    public async Task Reset_WrongTokenOrUnknownEmail_ReturnsSameGeneric400()
    {
        using var client = _factory.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAndLoginAsync(client, email);

        using var wrongToken = await ResetAsync(client, email, "not-a-valid-token");
        using var unknownEmail = await ResetAsync(client, AuthApi.UniqueEmail(), "not-a-valid-token");

        Assert.Equal(HttpStatusCode.BadRequest, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownEmail.StatusCode);
        var a = await wrongToken.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        var b = await unknownEmail.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(a!.Errors, b!.Errors);
    }

    [Fact]
    public async Task Reset_TokenFromAnotherAccount_IsRejected()
    {
        using var client = _factory.CreateApiClient();
        var first = AuthApi.UniqueEmail();
        var second = AuthApi.UniqueEmail();
        await AuthApi.RegisterAndLoginAsync(client, first);
        await AuthApi.RegisterAndLoginAsync(client, second);
        await ForgotAsync(client, first);
        var token = TokenFrom(_sender.Sent.Single().Link);

        using var response = await ResetAsync(client, second, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_ExpiredToken_IsRejected()
    {
        using var shortLived = new ResetFactory(_factory.Postgres, _sender, TimeSpan.FromMilliseconds(1));
        await shortLived.SeedIdentityAsync();
        using var client = shortLived.CreateApiClient();
        var email = AuthApi.UniqueEmail();
        await AuthApi.RegisterAndLoginAsync(client, email);
        await ForgotAsync(client, email);
        var token = TokenFrom(_sender.Sent.Single().Link);
        await Task.Delay(100);

        using var response = await ResetAsync(client, email, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void TokenLifetime_IsOneHour()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataProtectionTokenProviderOptions>>();

        Assert.Equal(TimeSpan.FromHours(1), options.Value.TokenLifespan);
    }

    private sealed class CapturingSender : IPasswordResetSender
    {
        public ConcurrentQueue<(string To, string Link)> Sent { get; } = new();

        public Task SendAsync(string email, string resetLink, CancellationToken cancellationToken)
        {
            Sent.Enqueue((email, resetLink));
            return Task.CompletedTask;
        }
    }

    private sealed class ResetFactory(PostgresFixture postgres, IPasswordResetSender sender, TimeSpan? lifespan) : JwtApiFactory(postgres)
    {
        public PostgresFixture Postgres { get; } = postgres;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(sender);
                if (lifespan is { } span)
                {
                    services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = span);
                }
            });
        }
    }
}
