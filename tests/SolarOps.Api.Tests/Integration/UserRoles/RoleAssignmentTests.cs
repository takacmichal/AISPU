using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SolarOps.Api.Features.Auth;
using SolarOps.Api.Features.UserRoles;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Auth;

namespace SolarOps.Api.Tests.Integration.UserRoles;

[Collection(PostgresCollection.Name)]
public sealed class RoleAssignmentTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JwtApiFactory _factory = new(postgres);

    public Task InitializeAsync() => _factory.SeedIdentityAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static IEnumerable<string> RolesOf(string accessToken) =>
        new JsonWebToken(accessToken).Claims.Where(c => c.Type == "role").Select(c => c.Value);

    private async Task AddToRoleAsync(string email, params string[] roles)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
    }

    /// <summary>Registers a user and logs in; the optional extra roles are added before login.</summary>
    private async Task<(Guid Id, string Email, HttpClient Client)> SignInAsync(params string[] extraRoles)
    {
        var email = AuthApi.UniqueEmail();
        using var anonymous = _factory.CreateApiClient();
        var register = await AuthApi.RegisterAsync(anonymous, email);
        var id = (await register.Content.ReadFromJsonAsync<UserResponse>())!.Id;
        if (extraRoles.Length > 0)
        {
            await AddToRoleAsync(email, extraRoles);
        }

        var tokens = await AuthApi.RegisterAndLoginAsync(anonymous, email);
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (id, email, client);
    }

    private static Task<HttpResponseMessage> SetRolesAsync(HttpClient client, Guid userId, params string[] roles) =>
        client.PutAsJsonAsync($"/api/users/{userId}/roles", new { roles });

    [Fact]
    public async Task Anonymous_Gets401()
    {
        using var client = _factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetRolesAsync(client, Guid.NewGuid(), Roles.Sales)).StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Gets403_OnEveryEndpoint_AndCannotChangeOwnRoles()
    {
        var (id, _, sales) = await SignInAsync(Roles.Sales);
        using var _ = sales;

        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync($"/api/users/{id}/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetRolesAsync(sales, id, Roles.Admin)).StatusCode);
    }

    [Fact]
    public async Task ListRoles_ReturnsAllApplicationRoles()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        using var _ = admin;

        var roles = await admin.GetFromJsonAsync<string[]>("/api/roles");

        Assert.Equal(Roles.All, roles);
    }

    [Fact]
    public async Task SetRoles_AssignsMultipleRoles_AndTheyAppearInNewJwt()
    {
        var (adminId, _, admin) = await SignInAsync(Roles.Admin);
        var (userId, userEmail, _) = await SignInAsync();
        using var _ = admin;

        var response = await SetRolesAsync(admin, userId, Roles.Sales, Roles.Designer, Roles.Customer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserRolesResponse>();
        Assert.Equal(userId, body!.Id);
        Assert.Equal(userEmail, body.Email);
        Assert.Equal([Roles.Customer, Roles.Designer, Roles.Sales], body.Roles.Order());

        using var anonymous = _factory.CreateApiClient();
        var login = await AuthApi.RegisterAndLoginAsync(anonymous, userEmail);
        Assert.Equal([Roles.Customer, Roles.Designer, Roles.Sales], RolesOf(login.AccessToken).Order());

        var got = await admin.GetFromJsonAsync<UserRolesResponse>($"/api/users/{userId}/roles");
        Assert.Equal([Roles.Customer, Roles.Designer, Roles.Sales], got!.Roles.Order());
        Assert.NotEqual(adminId, userId);
    }

    [Fact]
    public async Task SetRoles_RemovesRolesNotInRequest()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        var (userId, userEmail, _) = await SignInAsync(Roles.Sales, Roles.Technician);
        using var _ = admin;

        var response = await SetRolesAsync(admin, userId, Roles.Technician);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var anonymous = _factory.CreateApiClient();
        var login = await AuthApi.RegisterAndLoginAsync(anonymous, userEmail);
        Assert.Equal([Roles.Technician], RolesOf(login.AccessToken));
    }

    [Fact]
    public async Task SetRoles_AcceptsCaseInsensitiveNames_AndStoresCanonical()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        var (userId, _, _) = await SignInAsync();
        using var _ = admin;

        var response = await SetRolesAsync(admin, userId, "sales", "SALES");

        var body = await response.Content.ReadFromJsonAsync<UserRolesResponse>();
        Assert.Equal([Roles.Sales], body!.Roles);
    }

    [Theory]
    [InlineData("Obchod")]
    [InlineData("")]
    public async Task SetRoles_UnknownRole_Returns400_AndChangesNothing(string role)
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        var (userId, _, _) = await SignInAsync();
        using var _ = admin;

        var response = await SetRolesAsync(admin, userId, Roles.Sales, role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var got = await admin.GetFromJsonAsync<UserRolesResponse>($"/api/users/{userId}/roles");
        Assert.Equal([Roles.Customer], got!.Roles);
    }

    [Fact]
    public async Task SetRoles_EmptyList_Returns400()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        var (userId, _, _) = await SignInAsync();
        using var _ = admin;

        Assert.Equal(HttpStatusCode.BadRequest, (await SetRolesAsync(admin, userId)).StatusCode);
    }

    [Fact]
    public async Task UnknownUser_Returns404()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        using var _ = admin;

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/users/{Guid.NewGuid()}/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetRolesAsync(admin, Guid.NewGuid(), Roles.Sales)).StatusCode);
    }

    [Fact]
    public async Task SetRoles_CannotRemoveAdminFromLastAdmin()
    {
        // The seeded host has no other admin, so the only admin is the caller.
        var (adminId, _, admin) = await SignInAsync(Roles.Admin);
        using var _ = admin;

        var response = await SetRolesAsync(admin, adminId, Roles.Sales);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var got = await admin.GetFromJsonAsync<UserRolesResponse>($"/api/users/{adminId}/roles");
        Assert.Contains(Roles.Admin, got!.Roles);
    }

    [Fact]
    public async Task SetRoles_CanRemoveAdmin_WhenAnotherAdminExists()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        var (otherId, _, _) = await SignInAsync(Roles.Admin);
        using var _ = admin;

        var response = await SetRolesAsync(admin, otherId, Roles.Sales);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_ReturnsUsersWithRoles_FilteredByEmail_AndPaged()
    {
        var (_, _, admin) = await SignInAsync(Roles.Admin);
        var (userId, userEmail, _) = await SignInAsync(Roles.Sales);
        using var _ = admin;

        var page = await admin.GetFromJsonAsync<UserRolesListResponse>($"/api/users?q={userEmail[..20]}");

        var item = Assert.Single(page!.Items);
        Assert.Equal(userId, item.Id);
        Assert.Equal([Roles.Customer, Roles.Sales], item.Roles.Order());
        Assert.Equal(1, page.TotalCount);

        var all = await admin.GetFromJsonAsync<UserRolesListResponse>("/api/users?page=1&pageSize=1");
        Assert.Single(all!.Items);
        Assert.True(all.TotalCount >= 2);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/users?pageSize=1000")).StatusCode);
    }
}
