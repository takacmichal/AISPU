using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class TestAuthenticationTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    // Returns the principal produced by the default authentication scheme.
    private async Task<ClaimsPrincipal> AuthenticateAsync(Action<IHeaderDictionary> setHeaders)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        setHeaders(httpContext.Request.Headers);

        var result = await httpContext.AuthenticateAsync();
        return result.Succeeded ? result.Principal : new ClaimsPrincipal(new ClaimsIdentity());
    }

    // HttpContextAccessor is AsyncLocal-based, so it must be created in the calling method.
    private static HttpContextCurrentUser CurrentUserFor(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

    [Fact]
    public async Task DefaultScheme_ReadsUserAndRolesFromTestHeaders()
    {
        var userId = Guid.CreateVersion7();

        var user = CurrentUserFor(await AuthenticateAsync(h =>
        {
            h[TestAuthHandler.UserHeader] = userId.ToString();
            h[TestAuthHandler.RolesHeader] = $"{Roles.Sales},{Roles.Designer}";
        }));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(userId, user.UserId);
        Assert.True(user.IsInRole(Roles.Sales));
        Assert.True(user.IsInRole(Roles.Designer));
        Assert.False(user.IsInRole(Roles.Admin));
    }

    [Fact]
    public async Task DefaultScheme_WithoutHeaders_IsAnonymous()
    {
        var user = CurrentUserFor(await AuthenticateAsync(_ => { }));

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
    }

    [Fact]
    public async Task AuthenticatedClient_CanCallApi()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

        var response = await client.GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
