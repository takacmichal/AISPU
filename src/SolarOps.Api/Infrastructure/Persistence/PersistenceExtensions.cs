using Microsoft.EntityFrameworkCore;

namespace SolarOps.Api.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        // The connection string is resolved lazily so tests can override it via configuration.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");

            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres");
        return services;
    }

    /// <summary>
    /// Creates the schema from the current model (no migrations during Sprint 1).
    /// </summary>
    public static async Task EnsureDatabaseCreatedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
