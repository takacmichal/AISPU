using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.System;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class FoundationEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_ReturnsOk_WhenDatabaseIsReachable()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SystemInfo_ReturnsNameAndVersion()
    {
        using var client = _factory.CreateApiClient();

        var info = await client.GetFromJsonAsync<SystemInfoResponse>("/api/system/info");

        Assert.NotNull(info);
        Assert.Equal("SolarOps API", info.Name);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.Equal(ApiFactory.EnvironmentName, info.Environment);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails404()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task EnsureCreated_CreatesTablesAndColumnsInSnakeCase()
    {
        var columns = await _factory.ExecuteDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'users'")
            .ToListAsync());

        Assert.Contains("user_name", columns);
        Assert.Contains("normalized_email", columns);
    }
}
