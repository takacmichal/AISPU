using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SolarOps.Api.Infrastructure.Jobs;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Infrastructure;

/// <summary>Jobs run once by Container Apps Jobs with <c>--job &lt;name&gt;</c> (ADR #277).</summary>
public sealed class JobsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Api_RegistersTheDeploymentJobs()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var names = scope.ServiceProvider.GetServices<IJob>().Select(j => j.Name).Order().ToList();

        Assert.Equal(["migrate", "ml-analysis", "weather-refresh"], names);
    }

    [Fact]
    public async Task Migrate_OnEmptyDatabase_AppliesAllMigrations()
    {
        var databaseName = $"test_empty_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.CreateConnectionString(databaseName))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppDbContext(options);

        var succeeded = await new MigrateJob(db).RunAsync(CancellationToken.None);

        Assert.True(succeeded);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Migrate_OnMigratedDatabase_Succeeds()
    {
        Assert.Equal(JobRunner.Succeeded, await _factory.Services.RunJobAsync("migrate"));
    }

    [Fact]
    public async Task MlAnalysis_WithoutMlService_Fails()
    {
        // The test host has no Ml:BaseUrl; a job that cannot reach the service must not report success.
        Assert.Equal(JobRunner.Failed, await _factory.Services.RunJobAsync("ml-analysis"));
    }

    [Fact]
    public async Task WeatherRefresh_WithoutInstallations_Succeeds_EvenWhenTheScheduleIsOff()
    {
        // The test host sets Weather:Enabled=false, which only turns off the in-process schedule.
        Assert.Equal(JobRunner.Succeeded, await _factory.Services.RunJobAsync("weather-refresh"));
    }
}
