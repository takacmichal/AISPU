using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.OpenApi;

/// <summary>
/// The OpenAPI document (#145) is the single contract the frontend generates its types from, so it must be served
/// and every operation must carry a summary and at least one documented response.
/// </summary>
public sealed class OpenApiDocumentTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        // The document endpoint is mapped in the Development environment, or when OpenApi:Enabled is true.
        using var development = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabled_OutsideDevelopmentByDefault()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar/v1")).StatusCode);
    }

    [Fact]
    public async Task Enabled_OutsideDevelopment_ServesDocumentAndScalarAnonymously()
    {
        using var enabled = _factory.WithWebHostBuilder(builder => builder.UseSetting("OpenApi:Enabled", "true"));
        using var client = enabled.CreateClient();

        var document = await client.GetAsync("/openapi/v1.json");
        var scalar = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
    }

    [Fact]
    public async Task ExplicitlyDisabled_OutsideDevelopment_ReturnsNotFound()
    {
        using var disabled = _factory.WithWebHostBuilder(builder => builder.UseSetting("OpenApi:Enabled", "false"));
        using var client = disabled.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Document_IsServedAsOpenApi3WithApiPaths()
    {
        using var document = await GetDocumentAsync();

        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        Assert.Contains(document.RootElement.GetProperty("paths").EnumerateObject(), p => p.Name.StartsWith("/api/"));
    }

    [Fact]
    public async Task EveryOperation_HasSummaryAndDocumentedResponses()
    {
        using var document = await GetDocumentAsync();
        var missing = new List<string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var name = $"{operation.Name.ToUpperInvariant()} {path.Name}";
                if (!operation.Value.TryGetProperty("summary", out var summary) || string.IsNullOrWhiteSpace(summary.GetString()))
                {
                    missing.Add($"{name}: summary");
                }

                if (!operation.Value.TryGetProperty("responses", out var responses) || !responses.EnumerateObject().Any())
                {
                    missing.Add($"{name}: responses");
                }
            }
        }

        Assert.True(missing.Count == 0, "OpenAPI operations without documentation:\n" + string.Join('\n', missing));
    }
}
