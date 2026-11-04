using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Security;

/// <summary>Rate limiting of the login endpoint and security response headers (#139).</summary>
public sealed class RateLimitAndHeadersTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const int LoginLimit = 3;

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private HttpClient CreateLimitedClient() => _factory
        .WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Auth:PermitLimit", LoginLimit.ToString()))
        .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static Task<HttpResponseMessage> Login(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "Wrong-Password-1" });

    [Fact]
    public async Task Login_ReturnsTooManyRequests_AfterTheLimitIsExceeded()
    {
        using var client = CreateLimitedClient();

        for (var i = 0; i < LoginLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client)).StatusCode);
        }

        var limited = await Login(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter.Single()) > 0);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OtherEndpoints_AreNotAffectedByTheLoginLimit()
    {
        using var client = CreateLimitedClient();
        for (var i = 0; i <= LoginLimit; i++)
        {
            await Login(client);
        }

        var response = await client.GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/system/info");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Https_ResponsesCarryStrictTransportSecurity()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://api.solarops.test"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/system/info");

        Assert.Contains("max-age=", response.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public async Task ErrorResponses_CarrySecurityHeadersToo()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
