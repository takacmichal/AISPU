using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Security;

/// <summary>
/// Validation and binding failures answer with the same RFC 7807 body (#138): <c>application/problem+json</c>
/// with an <c>errors</c> dictionary for field errors and a <c>traceId</c> for support.
/// </summary>
public sealed class ValidationProblemDetailsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    [Fact]
    public async Task InvalidDto_Returns400ValidationProblemDetails()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/customers", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.Contains("Email", problem.Errors.Keys);
        Assert.Contains("Address", problem.Errors.Keys);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task NestedValidation_ReturnsIndexedFieldErrors()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/quotes", new
        {
            customerId = Guid.CreateVersion7(),
            lines = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("Lines", problem.Errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"email\":")]
    [InlineData("[]")]
    public async Task UnreadableBody_Returns400ProblemDetails(string body)
    {
        using var client = Sales();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/customers", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task InvalidQueryValue_Returns400ProblemDetails()
    {
        using var client = Sales();

        using var response = await client.GetAsync("/api/connection-requests?state=not-a-state");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
