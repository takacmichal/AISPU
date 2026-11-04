using Microsoft.Extensions.Logging;
using SolarOps.Api.Features.Weather;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Weather;

public sealed class WeatherForecastRefreshTests : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly FakeWeatherForecastClient _client = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WeatherApiFactory _factory;

    public WeatherForecastRefreshTests(PostgresFixture postgres)
    {
        _factory = new WeatherApiFactory(postgres, _client, _logs);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Refresh_Stores48HourlyRowsForInstallationWithCoordinates()
    {
        var installationId = await WeatherTestData.SeedInstallationAsync(
            _factory,
            Guid.CreateVersion7(),
            (48.15, 17.11, DateTimeOffset.UtcNow));

        var result = await WeatherTestData.RefreshAsync(_factory);

        Assert.Equal(new WeatherForecastRefreshResult(1, 1, 0, 0, 48), result);
        var rows = await WeatherTestData.ReadForecastsAsync(_factory, installationId);
        Assert.Equal(48, rows.Count);
        Assert.Equal(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), rows[0].ForecastTime);
        Assert.Equal(new DateTime(2026, 11, 4, 23, 0, 0, DateTimeKind.Utc), rows[^1].ForecastTime);
        Assert.Equal(DateTimeKind.Utc, rows[0].FetchedAt.Kind);
        var location = Assert.Single(_client.Locations);
        Assert.Equal(48.15, location.Latitude);
        Assert.Equal(17.11, location.Longitude);
    }

    [Fact]
    public async Task Refresh_SkipsInstallationWithoutCoordinates()
    {
        var installationId = await WeatherTestData.SeedInstallationAsync(_factory, Guid.CreateVersion7());

        var result = await WeatherTestData.RefreshAsync(_factory);

        Assert.Equal(new WeatherForecastRefreshResult(1, 0, 1, 0, 0), result);
        Assert.Equal(0, _client.Calls);
        Assert.Empty(await WeatherTestData.ReadForecastsAsync(_factory, installationId));
    }

    [Fact]
    public async Task Refresh_ReplacesPreviousForecastOnRerun()
    {
        var installationId = await WeatherTestData.SeedInstallationAsync(
            _factory,
            Guid.CreateVersion7(),
            (48.15, 17.11, DateTimeOffset.UtcNow));
        _client.ForecastFactory = FakeWeatherForecastClient.Forecast;

        await WeatherTestData.RefreshAsync(_factory);
        await WeatherTestData.RefreshAsync(_factory);

        var rows = await WeatherTestData.ReadForecastsAsync(_factory, installationId);
        Assert.Equal(48, rows.Count);
        Assert.Equal(2000d, rows[0].ShortwaveRadiationWm2);
    }

    [Fact]
    public async Task Refresh_RetriesApiFailureAndLogsWarning()
    {
        var installationId = await WeatherTestData.SeedInstallationAsync(
            _factory,
            Guid.CreateVersion7(),
            (48.15, 17.11, DateTimeOffset.UtcNow));
        _client.FailNext(1);

        var result = await WeatherTestData.RefreshAsync(_factory);

        Assert.Equal(new WeatherForecastRefreshResult(1, 1, 0, 0, 48), result);
        Assert.Equal(2, _client.Calls);
        Assert.Equal(48, (await WeatherTestData.ReadForecastsAsync(_factory, installationId)).Count);
        Assert.Contains(_logs.Entries, entry =>
            entry.Level == LogLevel.Warning
            && entry.Category == typeof(WeatherForecastRefreshService).FullName
            && entry.Message.Contains("retrying", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_LogsErrorAndKeepsPreviousForecastAfterFinalFailure()
    {
        var installationId = await WeatherTestData.SeedInstallationAsync(
            _factory,
            Guid.CreateVersion7(),
            (48.15, 17.11, DateTimeOffset.UtcNow));
        await WeatherTestData.RefreshAsync(_factory);

        _client.FailNext(3);
        var result = await WeatherTestData.RefreshAsync(_factory);

        Assert.Equal(new WeatherForecastRefreshResult(1, 0, 0, 1, 0), result);
        Assert.Equal(4, _client.Calls);
        var rows = await WeatherTestData.ReadForecastsAsync(_factory, installationId);
        Assert.Equal(48, rows.Count);
        Assert.Equal(1000d, rows[0].ShortwaveRadiationWm2);
        Assert.Contains(_logs.Entries, entry =>
            entry.Level == LogLevel.Error
            && entry.Category == typeof(WeatherForecastRefreshService).FullName
            && entry.Message.Contains("after 3 attempt(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_EmptyResponseKeepsPreviousForecast()
    {
        var installationId = await WeatherTestData.SeedInstallationAsync(
            _factory,
            Guid.CreateVersion7(),
            (48.15, 17.11, DateTimeOffset.UtcNow));
        await WeatherTestData.RefreshAsync(_factory);

        _client.ForecastFactory = _ => Array.Empty<WeatherForecastPoint>();
        var result = await WeatherTestData.RefreshAsync(_factory);

        Assert.Equal(new WeatherForecastRefreshResult(1, 0, 0, 1, 0), result);
        Assert.Equal(48, (await WeatherTestData.ReadForecastsAsync(_factory, installationId)).Count);
    }

    [Fact]
    public async Task Refresh_UsesCoordinatesOfNewestDesign()
    {
        var supplyPointId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await WeatherTestData.SeedInstallationAsync(
            _factory,
            supplyPointId,
            (48.00, 17.00, now.AddDays(-1)),
            (48.15, 17.11, now));

        await WeatherTestData.RefreshAsync(_factory);

        var location = Assert.Single(_client.Locations);
        Assert.Equal(48.15, location.Latitude);
        Assert.Equal(17.11, location.Longitude);
    }
}
