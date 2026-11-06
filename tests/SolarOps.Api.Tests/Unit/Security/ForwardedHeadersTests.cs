using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Infrastructure.Security;

namespace SolarOps.Api.Tests.Unit.Security;

/// <summary>The rate limit counts the real client IP, and only a trusted proxy may name it (#379). No database needed.</summary>
public sealed class ForwardedHeadersTests
{
    private const string ProxyIp = "10.0.0.5";
    private const int Limit = 2;

    /// <summary>A host with the real security pipeline; every request arrives from <paramref name="connectionIp"/>.</summary>
    private static async Task<WebApplication> StartHostAsync(string connectionIp, params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))
                .Append(new("RateLimiting:Auth:PermitLimit", Limit.ToString())));
        builder.Services.AddProblemDetails();
        builder.Services.AddSecurityHardening(builder.Configuration);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(connectionIp);
            return next();
        });
        app.UseSecurityHardening();
        app.MapGet("/ip", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString());
        app.MapGet("/limited", () => "ok").RequireRateLimiting(SecurityExtensions.AuthPolicy);
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Get(string path, string? forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return request;
    }

    private static async Task<string> ClientIpAsync(WebApplication app, string? forwardedFor)
    {
        using var client = app.GetTestClient();
        var response = await client.SendAsync(Get("/ip", forwardedFor));
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpStatusCode> HitLimitedAsync(HttpClient client, string? forwardedFor) =>
        (await client.SendAsync(Get("/limited", forwardedFor))).StatusCode;

    [Fact]
    public async Task ForwardedFor_FromUntrustedConnection_IsIgnored()
    {
        await using var app = await StartHostAsync("203.0.113.9");

        Assert.Equal("203.0.113.9", await ClientIpAsync(app, "198.51.100.1"));
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", ProxyIp)]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8")]
    public async Task ForwardedFor_FromTrustedProxy_BecomesTheClientIp(string key, string value)
    {
        await using var app = await StartHostAsync(ProxyIp, (key, value));

        Assert.Equal("198.51.100.1", await ClientIpAsync(app, "198.51.100.1"));
    }

    [Fact]
    public async Task ForwardedFor_TakesTheEntryAddedByTheProxy_NotOneSpoofedByTheClient()
    {
        await using var app = await StartHostAsync(ProxyIp, ("ForwardedHeaders:KnownProxies:0", ProxyIp));

        Assert.Equal("198.51.100.1", await ClientIpAsync(app, "192.0.2.77, 198.51.100.1"));
    }

    [Fact]
    public async Task RateLimit_BehindTrustedProxy_CountsEachClientSeparately()
    {
        await using var app = await StartHostAsync(ProxyIp, ("ForwardedHeaders:KnownProxies:0", ProxyIp));
        using var client = app.GetTestClient();

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await HitLimitedAsync(client, "198.51.100.1"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await HitLimitedAsync(client, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.OK, await HitLimitedAsync(client, "198.51.100.2"));
    }

    [Fact]
    public async Task RateLimit_CannotBeBypassed_ByChangingTheHeaderOnAnUntrustedConnection()
    {
        await using var app = await StartHostAsync("203.0.113.9");
        using var client = app.GetTestClient();

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await HitLimitedAsync(client, $"198.51.100.{i}"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await HitLimitedAsync(client, "198.51.100.200"));
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    public async Task InvalidProxyConfiguration_FailsAtStartup(string key, string value)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var app = await StartHostAsync(ProxyIp, (key, value));
            await ClientIpAsync(app, null);
        });
    }
}
