using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;

namespace SolarOps.Api.Tests.Unit;

public sealed class CurrentUserTests
{
    private static HttpContextCurrentUser For(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

    [Fact]
    public void Anonymous_HasNoUserId()
    {
        var user = For(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
        Assert.Empty(user.Roles);
        Assert.Throws<UnauthorizedAccessException>(() => user.RequireUserId());
    }

    [Fact]
    public void Authenticated_ReadsIdAndRoles()
    {
        var id = Guid.CreateVersion7();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, Roles.Sales)],
            authenticationType: "Test");

        var user = For(new ClaimsPrincipal(identity));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(id, user.UserId);
        Assert.True(user.IsInRole(Roles.Sales));
        Assert.False(user.IsInRole(Roles.Admin));
        Assert.Equal([Roles.Sales], user.Roles);
    }
}
