using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Auth;

/// <summary>Idempotent seeding of identity data: application roles and the first administrator.</summary>
public sealed class IdentitySeeder(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<AppUser> userManager,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    /// <summary>E-mail of the first administrator (ADR #277).</summary>
    public const string AdminEmail = "admin@solarops.local";

    /// <summary>
    /// Password of the first administrator: a secret (env <c>Admin__Password</c>, Key Vault or user-secrets), never in git.
    /// Without it no administrator is created.
    /// </summary>
    public const string AdminPasswordKey = "Admin:Password";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var role in Roles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create role {role}: {string.Join("; ", result.Errors.Select(e => e.Code))}");
            }

            logger.LogInformation("Created role {Role}", role);
        }
    }

    /// <summary>
    /// Creates <see cref="AdminEmail"/> with the Admin role. An existing account is never changed: one registered
    /// through the API before the secret was set must not become an administrator.
    /// </summary>
    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var password = configuration[AdminPasswordKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("{Key} is not set, no administrator account is created", AdminPasswordKey);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var existing = await userManager.FindByEmailAsync(AdminEmail);
        if (existing is not null)
        {
            if (!await userManager.IsInRoleAsync(existing, Roles.Admin))
            {
                logger.LogWarning("Account {Email} exists without the {Role} role and is not promoted", AdminEmail, Roles.Admin);
            }

            return;
        }

        var admin = new AppUser { UserName = AdminEmail, Email = AdminEmail };
        // Error codes only: the password must not reach the logs.
        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create administrator {AdminEmail}: {string.Join("; ", created.Errors.Select(e => e.Code))}");
        }

        var role = await userManager.AddToRoleAsync(admin, Roles.Admin);
        if (!role.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not add {AdminEmail} to role {Roles.Admin}: {string.Join("; ", role.Errors.Select(e => e.Code))}");
        }

        logger.LogInformation("Created administrator {Email}", AdminEmail);
    }
}

/// <summary>
/// Runs <see cref="IdentitySeeder"/> at startup once the schema exists
/// (Development creates it before the host starts; without schema the seeding is skipped).
/// </summary>
internal sealed class IdentitySeedHostedService(IServiceScopeFactory scopeFactory, ILogger<IdentitySeedHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var creator = db.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(cancellationToken) || !await creator.HasTablesAsync(cancellationToken))
            {
                logger.LogInformation("Database schema does not exist yet, identity seeding skipped");
                return;
            }

            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The API must still start (e.g. database temporarily unavailable); /health reports the database state.
            logger.LogError(ex, "Identity seeding failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
