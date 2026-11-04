using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Security;

/// <summary>
/// Browser CORS policy (#138): only origins listed in <c>Cors:AllowedOrigins</c> are allowed,
/// the list is supplied by configuration and the server answers preflight requests.
/// </summary>
public sealed class CorsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string AllowedOrigin = "https://app.solarops.test";
    private const string OtherOrigin = "https://other.solarops.test";

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    /// <summary>The list is read from configuration, so a deployment can set it through environment variables.</summary>
    private WebApplicationFactory<Program> WithOrigins(params string[] origins) =>
        _factory.WithWebHostBuilder(builder =>
        {
            for (var i = 0; i < origins.Length; i++)
            {
                builder.UseSetting($"Cors:AllowedOrigins:{i}", origins[i]);
            }
        });

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/system/info");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }

    private static HttpRequestMessage GetWithOrigin(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("Origin", origin);
        return request;
    }

    [Fact]
    public async Task Preflight_FromConfiguredOrigin_IsAllowed()
    {
        using var factory = WithOrigins(AllowedOrigin);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Preflight(AllowedOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.True(response.Headers.Contains("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task ActualRequest_FromConfiguredOrigin_GetsAllowOriginHeader()
    {
        using var factory = WithOrigins(AllowedOrigin);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(GetWithOrigin(AllowedOrigin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task EveryConfiguredOrigin_IsAllowed_AndOthersAreNot()
    {
        using var factory = WithOrigins(AllowedOrigin, "http://localhost:5173");
        using var client = factory.CreateClient();

        using var allowed = await client.SendAsync(Preflight("http://localhost:5173"));
        using var denied = await client.SendAsync(Preflight(OtherOrigin));

        Assert.Equal("http://localhost:5173", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ActualRequest_FromUnconfiguredOrigin_GetsNoCorsHeaders()
    {
        using var factory = WithOrigins(AllowedOrigin);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(GetWithOrigin(OtherOrigin));

        // The API still answers (CORS is enforced by the browser), but the browser cannot read the response.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task EmptyConfiguration_AllowsNoOrigin()
    {
        // appsettings.json ships with an empty list; only appsettings.Development.json adds localhost.
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Preflight(AllowedOrigin));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
