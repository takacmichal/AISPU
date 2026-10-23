using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Auth;

/// <summary>Idempotent seeding of identity data (application roles).</summary>
public sealed class IdentitySeeder(RoleManager<IdentityRole<Guid>> roleManager, ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
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
