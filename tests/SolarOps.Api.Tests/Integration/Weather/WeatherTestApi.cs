using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Weather;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Weather;

/// <summary>ApiFactory whose weather client is replaced by a fake and whose retries do not sleep.</summary>
internal sealed class WeatherApiFactory(
    PostgresFixture postgres,
    IWeatherForecastClient client,
    RecordingLoggerProvider? logs = null) : ApiFactory(postgres)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Weather:MaxAttempts", "3");
        builder.UseSetting("Weather:RetryDelaySeconds", "0");
        if (logs is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWeatherForecastClient>();
            services.AddSingleton(client);
        });
    }
}

internal sealed class FakeWeatherForecastClient : IWeatherForecastClient
{
    private readonly Queue<Exception> _failures = new();

    public int Calls { get; private set; }

    public List<(double Latitude, double Longitude)> Locations { get; } = [];

    /// <summary>Builds the response for the given call number (1-based), so a rerun can return different values.</summary>
    public Func<int, IReadOnlyList<WeatherForecastPoint>> ForecastFactory { get; set; } = call => Forecast(1);

    public void FailNext(int count, Exception? exception = null)
    {
        for (var i = 0; i < count; i++)
        {
            _failures.Enqueue(exception ?? new HttpRequestException("Open-Meteo is unavailable"));
        }
    }

    public Task<IReadOnlyList<WeatherForecastPoint>> GetForecastAsync(
        double latitude,
        double longitude,
        CancellationToken ct)
    {
        Calls++;
        Locations.Add((latitude, longitude));
        if (_failures.Count > 0)
        {
            throw _failures.Dequeue();
        }

        return Task.FromResult(ForecastFactory(Calls));
    }

    public static IReadOnlyList<WeatherForecastPoint> Forecast(int seed) =>
        Enumerable.Range(0, 48)
            .Select(i => new WeatherForecastPoint(
                new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                seed * 1000 + i,
                seed * 1000 + i + 1,
                seed * 1000 + i + 2,
                i,
                10 + i,
                1 + i))
            .ToList();
}

internal static class WeatherTestData
{
    /// <summary>
    /// Seeds one installation for <paramref name="supplyPointId"/>. Each tuple adds an FVE design with coordinates;
    /// no tuple means the installation has no location and must be skipped by the refresh.
    /// </summary>
    public static Task<Guid> SeedInstallationAsync(
        ApiFactory factory,
        Guid supplyPointId,
        params (double Latitude, double Longitude, DateTimeOffset CreatedAt)[] designs) =>
        factory.ExecuteDbAsync(async db =>
        {
            var userId = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            var installation = Installation.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                supplyPointId,
                "FVE test",
                5m,
                null,
                userId,
                now);
            db.Add(installation);

            foreach (var design in designs)
            {
                db.Add(NewDesign(supplyPointId, design.Latitude, design.Longitude, design.CreatedAt, userId));
            }

            await db.SaveChangesAsync();
            return installation.Id;
        });

    public static async Task<WeatherForecastRefreshResult> RefreshAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IWeatherForecastRefreshService>()
            .RefreshAsync(CancellationToken.None);
    }

    public static Task<List<WeatherForecast>> ReadForecastsAsync(ApiFactory factory, Guid installationId) =>
        factory.ExecuteDbAsync(db => db.Set<WeatherForecast>()
            .Where(f => f.InstallationId == installationId)
            .OrderBy(f => f.ForecastTime)
            .ToListAsync());

    private static FveDesign NewDesign(
        Guid supplyPointId,
        double latitude,
        double longitude,
        DateTimeOffset createdAt,
        Guid userId)
    {
        var design = FveDesign.Request(Guid.CreateVersion7(), supplyPointId, null, userId, createdAt);
        design.RecordSiteSurvey("Strecha v poriadku", userId, createdAt);
        design.UpdateParameters(
            latitude,
            longitude,
            4500,
            ConsumptionProfile.Household,
            [
                new RoofPlane
                {
                    Name = "Juh",
                    AzimuthDeg = 0,
                    TiltDeg = 35,
                    UsableAreaM2 = 30,
                    ShadingLossPercent = 0,
                    RoofType = RoofType.PitchedTile,
                },
            ],
            createdAt);
        return design;
    }
}
