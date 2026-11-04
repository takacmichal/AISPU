using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Production;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using InstallationsTestData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Production;

/// <summary>
/// Production overviews (#100 to #102) over seeded telemetry. Fixed data on 2026-06-21 (Europe/Bratislava is UTC+2):
/// <list type="bullet">
/// <item>inverter A: readings 09:00Z to 10:00Z every 5 min, totalWh +100 per reading, 1000 W (3000 W in the last window),
/// one more reading at 22:05Z (local 00:05 on 22 June) with totalWh +500;</item>
/// <item>inverter B (replaced afterwards): readings 09:00Z to 09:30Z, totalWh +50 per reading, 500 W;</item>
/// <item>an inverter of another installation with readings in the same hour (must not count).</item>
/// </list>
/// The 09:00Z readings close the window 08:55-09:00, so they are only the counter base of hour 09.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductionEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTime Day = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private static string Url(Guid installationId, string query, string path = "") =>
        $"/api/installations/{installationId}/production{path}?{query}";

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private sealed record Seed(Guid InstallationId, Guid OtherInstallationId, Guid CustomerUserId, Guid OtherCustomerUserId);

    private async Task<Seed> SeedAsync()
    {
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("a@example.sk") with { UserId = userA });
        var customerB = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("b@example.sk", lastName: "Kováč") with { UserId = userB });

        using var op = Operator();
        var installation = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest() with { CustomerId = customerA.Id })).Installation.Id;
        var other = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest() with { CustomerId = customerB.Id })).Installation.Id;

        var inverterA = await AddInverterAsync(op, installation, "INV-A");
        var inverterB = await AddInverterAsync(op, installation, "INV-B");
        var foreignInverter = await AddInverterAsync(op, other, "INV-X");

        var readings = new List<(Guid Device, DateTime Ts, float PowerW, double TotalWh)>();
        for (var i = 0; i <= 12; i++)
        {
            readings.Add((inverterA, Day.AddHours(9).AddMinutes(5 * i), i == 12 ? 3000 : 1000, 10_000 + (100 * i)));
            readings.Add((foreignInverter, Day.AddHours(9).AddMinutes(5 * i), 9000, 50_000 + (900 * i)));
        }

        for (var i = 0; i <= 6; i++)
        {
            readings.Add((inverterB, Day.AddHours(9).AddMinutes(5 * i), 500, 20_000 + (50 * i)));
        }

        readings.Add((inverterA, Day.AddHours(22).AddMinutes(5), 200, 11_700));
        await InsertReadingsAsync(readings);

        // Readings of a replaced inverter keep counting.
        using var replaced = await op.PostJsonAsync($"/api/installations/{installation}/devices/{inverterB}/replace", new ReplaceDeviceRequest
        {
            SerialNumber = "INV-B2",
            Manufacturer = "Fronius",
            Model = "Symo 10.0",
            PowerW = 10000,
            Reason = "Fault",
        });
        replaced.EnsureSuccessStatusCode();

        return new Seed(installation, other, userA, userB);
    }

    private static async Task<Guid> AddInverterAsync(HttpClient client, Guid installationId, string serial)
    {
        using var response = await client.PostJsonAsync($"/api/installations/{installationId}/devices", new AddDeviceRequest
        {
            Type = DeviceType.Inverter,
            SerialNumber = serial,
            Manufacturer = "Fronius",
            Model = "Symo 10.0",
            PowerW = 10000,
        });
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<DeviceResponse>()).Id;
    }

    private Task InsertReadingsAsync(IReadOnlyList<(Guid Device, DateTime Ts, float PowerW, double TotalWh)> readings)
    {
        var devices = readings.Select(r => r.Device).ToArray();
        var timestamps = readings.Select(r => r.Ts).ToArray();
        var power = readings.Select(r => r.PowerW).ToArray();
        var total = readings.Select(r => r.TotalWh).ToArray();
        return _factory.ExecuteDbAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO telemetry_readings (device_id, ts, interval_sec, status, events, ac_power_w, total_wh, source, received_at)
            SELECT r.device_id, r.ts, 300, 4, '[]'::jsonb, r.ac_power_w, r.total_wh, 'simulator', r.ts
            FROM unnest({devices}::uuid[], {timestamps}::timestamptz[], {power}::real[], {total}::double precision[])
                AS r(device_id, ts, ac_power_w, total_wh)
            """));
    }

    private async Task<Guid> NewUserAsync()
    {
        var user = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user.Id;
    }

    [Fact]
    public async Task Hourly_SumsTheInverters_FromTheirCounters()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-06-21T09:00:00Z&to=2026-06-21T10:00:00Z"));

        Assert.Equal(ProductionGranularity.Hour, result.Granularity);
        Assert.Equal("Europe/Bratislava", result.TimeZone);
        var point = Assert.Single(result.Points);
        Assert.Equal(new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero), point.PeriodStart);
        Assert.Equal(1.5, point.EnergyKwh);   // A 12 x 100 Wh + B 6 x 50 Wh
        Assert.Equal(3.0, point.PeakPowerKw); // the last window of A; A + B together give only 1.5 kW
        Assert.Equal(12, point.SampleCount);
        Assert.Equal(1.5, result.TotalEnergyKwh);
        Assert.Equal(3.0, result.PeakPowerKw);
    }

    [Fact]
    public async Task FiveMinutes_ReturnsOnePointPerWindow()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-06-21T09:00:00Z&to=2026-06-21T09:15:00Z&granularity=fiveMinutes"));

        Assert.Equal(
            [new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 21, 9, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 21, 9, 10, 0, TimeSpan.Zero)],
            result.Points.Select(p => p.PeriodStart));
        Assert.All(result.Points, p =>
        {
            Assert.Equal(0.15, p.EnergyKwh);
            Assert.Equal(1.5, p.PeakPowerKw);
            Assert.Equal(1, p.SampleCount);
        });
    }

    [Fact]
    public async Task Daily_UsesTheLocalCalendarDay()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-06-21&to=2026-06-23&granularity=day"));

        // 22:05Z is 00:05 on 22 June in Bratislava, so its window belongs to the next local day.
        Assert.Equal(
            [(new DateTimeOffset(2026, 6, 20, 22, 0, 0, TimeSpan.Zero), 1.5), (new DateTimeOffset(2026, 6, 21, 22, 0, 0, TimeSpan.Zero), 0.5)],
            result.Points.Select(p => (p.PeriodStart, p.EnergyKwh)));
        Assert.Equal(2.0, result.TotalEnergyKwh);
    }

    [Fact]
    public async Task Daily_InUtc_PutsEverythingIntoOneDay()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-06-21&to=2026-06-23&granularity=day&timeZone=UTC"));

        var point = Assert.Single(result.Points);
        Assert.Equal(new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), point.PeriodStart);
        Assert.Equal(2.0, point.EnergyKwh);
    }

    [Fact]
    public async Task Monthly_SumsTheWholeMonth()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-01-01&to=2027-01-01&granularity=month"));

        var point = Assert.Single(result.Points);
        Assert.Equal(new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero), point.PeriodStart);
        Assert.Equal(2.0, point.EnergyKwh);
        Assert.Equal(14, point.SampleCount); // 12 windows of hour 09, the 08:55 window (first readings, no counter base, 0 Wh) and the night one
    }

    [Fact]
    public async Task EmptyPeriod_ReturnsAnEmptyList()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-07-01&to=2026-07-02"));

        Assert.Empty(result.Points);
        Assert.Equal(0, result.TotalEnergyKwh);
        Assert.Null(result.PeakPowerKw);
    }

    [Fact]
    public async Task InstallationWithoutInverters_ReturnsAnEmptyList()
    {
        using var op = Operator();
        var installation = (await InstallationsTestData.CreateInstallationAsync(op)).Installation.Id;

        var result = await op.GetJsonAsync<ProductionResponse>(Url(installation, "from=2026-06-21&to=2026-06-22"));

        Assert.Empty(result.Points);
    }

    [Fact]
    public async Task UnknownInstallation_Returns404()
    {
        using var client = Operator();

        using var response = await client.GetAsync(Url(Guid.CreateVersion7(), "from=2026-06-21&to=2026-06-22"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("from=2026-06-21")]
    [InlineData("from=2026-06-21&to=2026-06-22&granularity=week")]
    [InlineData("from=2026-01-01&to=2026-03-01&granularity=fiveMinutes")]
    [InlineData("from=2026-06-21&to=2026-06-22&timeZone=Mars%2FOlympus")]
    public async Task InvalidQuery_Returns400(string query)
    {
        using var client = Operator();

        using var response = await client.GetAsync(Url(Guid.CreateVersion7(), query));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/comparison")]
    [InlineData("/export")]
    public async Task Anonymous_Returns401(string path)
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(Url(Guid.CreateVersion7(), "from=2026-06-21&to=2026-06-22", path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/comparison")]
    [InlineData("/export")]
    public async Task UserWithoutRole_Returns403(string path)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7());

        using var response = await client.GetAsync(Url(Guid.CreateVersion7(), "from=2026-06-21&to=2026-06-22", path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_SeesOwnInstallation()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(seed.CustomerUserId, Roles.Customer);

        var result = await client.GetJsonAsync<ProductionResponse>(Url(seed.InstallationId, "from=2026-06-21&to=2026-06-22"));

        Assert.Equal(1.5, result.TotalEnergyKwh);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/comparison")]
    [InlineData("/export")]
    public async Task Customer_GetsNotFoundForAnotherCustomersInstallation(string path)
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(seed.OtherCustomerUserId, Roles.Customer);

        using var response = await client.GetAsync(Url(seed.InstallationId, "from=2026-06-21&to=2026-06-22", path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Comparison_WithoutPrediction_ReturnsActualWithNullPrediction()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);

        var result = await client.GetJsonAsync<ProductionComparisonResponse>(
            Url(seed.InstallationId, "from=2026-06-21T09:00:00Z&to=2026-06-21T10:00:00Z", "/comparison"));

        Assert.False(result.PredictionAvailable);
        Assert.Null(result.ModelVersion);
        Assert.Null(result.PredictedEnergyKwh);
        Assert.Equal(1.5, result.ActualEnergyKwh);
        var point = Assert.Single(result.Points);
        Assert.Equal(1.5, point.ActualEnergyKwh);
        Assert.Null(point.PredictedEnergyKwh);
        Assert.Null(point.DeviationKwh);
        Assert.Null(point.Ratio);
    }

    [Fact]
    public async Task Comparison_PairsActualWithAPrediction()
    {
        var seed = await SeedAsync();
        var prediction = new ProductionPrediction("test-model", [
            new PredictedHour(new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero), 2000),
            new PredictedHour(new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero), 1000),
        ]);
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IProductionPredictionSource>(new FixedPredictionSource(prediction)))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.CreateVersion7().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Technician);

        var result = await client.GetJsonAsync<ProductionComparisonResponse>(
            Url(seed.InstallationId, "from=2026-06-21T09:00:00Z&to=2026-06-21T11:00:00Z", "/comparison"));

        Assert.True(result.PredictionAvailable);
        Assert.Equal("test-model", result.ModelVersion);
        Assert.Equal(3.0, result.PredictedEnergyKwh);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal((1.5, 2.0, -0.5, 0.75), (result.Points[0].ActualEnergyKwh, result.Points[0].PredictedEnergyKwh, result.Points[0].DeviationKwh, result.Points[0].Ratio));
        Assert.Null(result.Points[1].ActualEnergyKwh);
        Assert.Equal(1.0, result.Points[1].PredictedEnergyKwh);
    }

    [Fact]
    public async Task Comparison_RejectsFiveMinutes()
    {
        using var client = Operator();

        using var response = await client.GetAsync(Url(Guid.CreateVersion7(), "from=2026-06-21&to=2026-06-22&granularity=fiveMinutes", "/comparison"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsCsvWithHeader_SemicolonAndDecimalCommaByDefault()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(seed.CustomerUserId, Roles.Customer);

        using var response = await client.GetAsync(Url(seed.InstallationId, "from=2026-06-21&to=2026-06-23&granularity=day", "/export"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "period_start_utc;period_start_local;energy_kwh;peak_power_kw;sample_count",
                "2026-06-20T22:00:00Z;2026-06-21 00:00;1,5;3;13",
                "2026-06-21T22:00:00Z;2026-06-22 00:00;0,5;0,2;1",
            ],
            lines);
    }

    [Fact]
    public async Task Export_UsesTheRequestedSeparator()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var csv = await client.GetStringAsync(Url(seed.InstallationId, "from=2026-06-21T09:00:00Z&to=2026-06-21T09:10:00Z&granularity=fiveMinutes&separator=comma", "/export"));

        Assert.Equal(
            "period_start_utc,period_start_local,energy_kwh,peak_power_kw,sample_count\r\n" +
            "2026-06-21T09:00:00Z,2026-06-21 11:00,0.15,1.5,1\r\n" +
            "2026-06-21T09:05:00Z,2026-06-21 11:05,0.15,1.5,1\r\n",
            csv);
    }

    [Fact]
    public async Task Export_EmptyPeriod_ReturnsOnlyTheHeader()
    {
        var seed = await SeedAsync();
        using var client = Operator();

        var csv = await client.GetStringAsync(Url(seed.InstallationId, "from=2026-07-01&to=2026-07-02&separator=tab", "/export"));

        Assert.Equal("period_start_utc\tperiod_start_local\tenergy_kwh\tpeak_power_kw\tsample_count\r\n", csv);
    }

    [Fact]
    public async Task Export_RejectsAnUnknownSeparator()
    {
        using var client = Operator();

        using var response = await client.GetAsync(Url(Guid.CreateVersion7(), "from=2026-06-21&to=2026-06-22&separator=pipe", "/export"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("separator", out _));
    }

    private sealed class FixedPredictionSource(ProductionPrediction prediction) : IProductionPredictionSource
    {
        public Task<ProductionPrediction> GetAsync(Guid installationId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult(prediction);
    }
}
