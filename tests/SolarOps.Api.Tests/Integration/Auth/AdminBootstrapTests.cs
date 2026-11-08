using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Features.Auth;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Auth;

/// <summary>The first administrator is created from the Admin:Password secret (ADR #277).</summary>
public sealed class AdminBootstrapTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string AdminPassword = "Admin-Bootstrap-2026";

    private readonly JwtApiFactory _factory = new(postgres);

    public Task InitializeAsync() => _factory.SeedIdentityAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Seed_WithAdminPassword_CreatesAdministratorWhoCanLogIn()
    {
        await using var host = WithAdminPassword(AdminPassword);
        await SeedAsync(host);

        using var client = host.CreateClient();
        var login = await AuthApi.LoginAsync(client, IdentitySeeder.AdminEmail, AdminPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal([Roles.Admin], await RequiredRolesOfAsync(host, IdentitySeeder.AdminEmail));
    }

    [Fact]
    public async Task Seed_WithoutAdminPassword_CreatesNoAdministrator()
    {
        await SeedAsync(_factory);

        Assert.Null(await RolesOfAsync(_factory, IdentitySeeder.AdminEmail));
    }

    [Fact]
    public async Task Seed_Twice_KeepsOneAdministrator()
    {
        await using var host = WithAdminPassword(AdminPassword);

        await SeedAsync(host);
        await SeedAsync(host);

        Assert.Equal([Roles.Admin], await RequiredRolesOfAsync(host, IdentitySeeder.AdminEmail));
        var count = await _factory.ExecuteDbAsync(db => db.Users.CountAsync(u => u.Email == IdentitySeeder.AdminEmail));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Seed_DoesNotPromote_AnAccountRegisteredWithTheAdminEmail()
    {
        using (var client = _factory.CreateApiClient())
        {
            var register = await AuthApi.RegisterAsync(client, IdentitySeeder.AdminEmail);
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        }

        await using var host = WithAdminPassword(AdminPassword);
        await SeedAsync(host);

        Assert.Equal([Roles.Customer], await RequiredRolesOfAsync(host, IdentitySeeder.AdminEmail));
        using var login = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthApi.LoginAsync(login, IdentitySeeder.AdminEmail, AdminPassword)).StatusCode);
    }

    [Fact]
    public async Task Seed_WeakAdminPassword_Throws_WithoutRevealingIt()
    {
        const string weak = "weakpass";
        await using var host = WithAdminPassword(weak);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(host));

        Assert.DoesNotContain(weak, error.Message);
        Assert.Null(await RolesOfAsync(host, IdentitySeeder.AdminEmail));
    }

    /// <summary>Host on the same database with the secret set (its startup seeding runs as well).</summary>
    private WebApplicationFactory<Program> WithAdminPassword(string password) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting(IdentitySeeder.AdminPasswordKey, password));

    private static async Task SeedAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
    }

    private static async Task<string[]> RequiredRolesOfAsync(WebApplicationFactory<Program> host, string email) =>
        await RolesOfAsync(host, email) ?? throw new Xunit.Sdk.XunitException($"User {email} does not exist");

    /// <summary>Roles of the user with <paramref name="email"/>, or null when there is no such user.</summary>
    private static async Task<string[]?> RolesOfAsync(WebApplicationFactory<Program> host, string email)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        return user is null ? null : [.. (await users.GetRolesAsync(user)).Order()];
    }
}
