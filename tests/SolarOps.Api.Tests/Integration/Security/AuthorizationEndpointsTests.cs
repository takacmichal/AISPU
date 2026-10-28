using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Security;

/// <summary>
/// End-to-end authorization checks (#136): every API endpoint is either explicitly anonymous or uses one
/// documented policy, and each policy answers 401 for anonymous callers and 403 for a role outside the matrix.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthorizationEndpointsTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>Endpoints that are anonymous on purpose; everything else must have a policy.</summary>
    private static readonly string[] AnonymousPrefixes = ["api/auth/", "api/system/"];

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    public static IEnumerable<object[]> Policies => AuthorizationPolicies.All.Select(policy => new object[] { policy });

    [Fact]
    public void EveryApiEndpoint_IsEitherAnonymousOrUsesADocumentedPolicy()
    {
        var endpoints = ApiEndpoints();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var route = RouteOf(endpoint);
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                Assert.Contains(AnonymousPrefixes, prefix => route.StartsWith(prefix, StringComparison.Ordinal));
                continue;
            }

            var policy = Assert.Single(PoliciesOf(endpoint));
            Assert.True(
                AuthorizationPolicies.Matrix.ContainsKey(policy),
                $"Endpoint '{route}' uses policy '{policy}' which is missing from {nameof(AuthorizationPolicies.Matrix)}.");
        }
    }

    [Fact]
    public void EveryPolicy_IsUsedByAtLeastOneEndpoint()
    {
        var used = ApiEndpoints().SelectMany(PoliciesOf).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            AuthorizationPolicies.All.Order(StringComparer.Ordinal),
            used.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task Policy_Returns401Anonymous_403ForOtherRole_AndAllowsItsRoles(string policy)
    {
        var endpoint = SampleEndpoint(policy);
        var request = RequestFor(endpoint);

        using var anonymous = _factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(request())).StatusCode);

        foreach (var role in AuthorizationPolicies.Matrix[policy])
        {
            using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);
            using var response = await client.SendAsync(request());
            Assert.False(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Role '{role}' is in policy '{policy}' but {endpoint.RoutePattern.RawText} returned {(int)response.StatusCode}.");
        }

        var deniedRole = Roles.All.FirstOrDefault(role => !AuthorizationPolicies.Matrix[policy].Contains(role));
        if (deniedRole is not null)
        {
            using var client = _factory.CreateApiClient(Guid.CreateVersion7(), deniedRole);
            using var response = await client.SendAsync(request());
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>All route endpoints under <c>/api</c> (health checks and OpenAPI are mapped elsewhere).</summary>
    private List<RouteEndpoint> ApiEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => RouteOf(endpoint).StartsWith("api/", StringComparison.Ordinal))
            .OrderBy(RouteOf, StringComparer.Ordinal)
            .ToList();

    private static string RouteOf(RouteEndpoint endpoint) =>
        (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

    private static List<string> PoliciesOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .Where(policy => !string.IsNullOrEmpty(policy))
            .Select(policy => policy!)
            .ToList();

    /// <summary>One endpoint of the policy: the fewest route parameters, GET preferred over a write.</summary>
    private RouteEndpoint SampleEndpoint(string policy)
    {
        var candidates = ApiEndpoints().Where(endpoint => PoliciesOf(endpoint).Contains(policy));
        return candidates
            .OrderBy(endpoint => RouteOf(endpoint).Count(c => c == '{'))
            .ThenBy(endpoint => MethodsOf(endpoint).Contains("GET", StringComparer.Ordinal) ? 0 : 1)
            .ThenBy(RouteOf, StringComparer.Ordinal)
            .First();
    }

    private static string[] MethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? [];

    /// <summary>A request factory, because an <see cref="HttpRequestMessage"/> can be sent only once.</summary>
    private static Func<HttpRequestMessage> RequestFor(RouteEndpoint endpoint)
    {
        var method = MethodsOf(endpoint).First(m => m is not "OPTIONS" and not "HEAD");
        var url = "/" + Regex.Replace(
            RouteOf(endpoint),
            @"\{([^}]+)\}",
            match => match.Groups[1].Value.Contains(":int", StringComparison.Ordinal)
                ? "1"
                : Guid.CreateVersion7().ToString());

        return () =>
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method is "POST" or "PUT" or "PATCH")
            {
                // The point is authorization, not the payload: an empty object fails validation after it.
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            return request;
        };
    }
}
