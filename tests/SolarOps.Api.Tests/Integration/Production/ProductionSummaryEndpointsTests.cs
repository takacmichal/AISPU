using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Production;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using InstallationsTestData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Production;

/// <summary>
/// <c>GET /api/production/summary</c>: production of all visible installations. Fixed data on 2026-06-21, 09:00Z to 09:30Z
/// (the 09:00Z reading only closes window 08:55 and is the counter base): installation A has one inverter with
/// 1000 W and +100 Wh per reading (6 windows, 0.6 kWh), installation B one inverter with 500 W and +50 Wh (6 windows, 0.3 kWh).
/// </summary>
public sealed class ProductionSummaryEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Summary = "/api/production/summary";
    private const string Period = "from=2026-06-21T09:00:00Z&to=2026-06-21T10:00:00Z";

    private static readonly DateTime Day = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private sealed record Seed(Guid CustomerUserA, Guid CustomerUserB);

    private async Task<Seed> SeedAsync()
    {
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("a@example.sk") with { UserId = userA });
        var customerB = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("b@example.sk", lastName: "Kováč") with { UserId = userB });

        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var installationA = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest() with { CustomerId = customerA.Id })).Installation.Id;
        var installationB = (await InstallationsTestData.CreateInstallationAsync(op, InstallationsTestData.NewInstallationRequest() with { CustomerId = customerB.Id })).Installation.Id;
        var inverterA = await AddInverterAsync(op, installationA, "SUM-A");
        var inverterB = await AddInverterAsync(op, installationB, "SUM-B");

        var devices = new List<Guid>();
        var timestamps = new List<DateTime>();
        var power = new List<float>();
        var total = new List<double>();
        for (var i = 0; i <= 6; i++)
        {
            var ts = Day.AddHours(9).AddMinutes(5 * i);
            devices.AddRange([inverterA, inverterB]);
            timestamps.AddRange([ts, ts]);
            power.AddRange([1000f, 500f]);
            total.AddRange([10_000 + (100 * i), 20_000 + (50 * i)]);
        }

        var deviceArray = devices.ToArray();
        var timestampArray = timestamps.ToArray();
        var powerArray = power.ToArray();
        var totalArray = total.ToArray();
        await _factory.ExecuteDbAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO telemetry_readings (device_id, ts, interval_sec, status, events, ac_power_w, total_wh, source, received_at)
            SELECT r.device_id, r.ts, 300, 4, '[]'::jsonb, r.ac_power_w, r.total_wh, 'simulator', r.ts
            FROM unnest({deviceArray}::uuid[], {timestampArray}::timestamptz[], {powerArray}::real[], {totalArray}::double precision[])
                AS r(device_id, ts, ac_power_w, total_wh)
            """));
        return new Seed(userA, userB);
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

    [Theory]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Technician)]
    public async Task Staff_SeesTheSumOfAllInstallations(string role)
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        var result = await client.GetJsonAsync<ProductionSummaryResponse>($"{Summary}?{Period}");

        Assert.Equal(2, result.InstallationCount);
        Assert.Equal(ProductionGranularity.Hour, result.Granularity);
        Assert.Equal(0.9, result.TotalEnergyKwh);
        Assert.Equal(1.5, result.PeakPowerKw);
        var point = Assert.Single(result.Points);
        Assert.Equal(new DateTimeOffset(Day.AddHours(9)), point.PeriodStart);
        Assert.Equal(0.9, point.EnergyKwh);
        Assert.Equal(1.5, point.PeakPowerKw);
        Assert.Equal(6, point.SampleCount);
    }

    [Fact]
    public async Task FiveMinutes_SumsTheInstallationsPerWindow()
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

        var result = await client.GetJsonAsync<ProductionSummaryResponse>($"{Summary}?{Period}&granularity=fiveMinutes");

        Assert.Equal(6, result.Points.Count);
        Assert.All(result.Points, p => Assert.Equal(0.15, p.EnergyKwh));
        Assert.All(result.Points, p => Assert.Equal(1.5, p.PeakPowerKw));
    }

    [Fact]
    public async Task Customer_SeesOnlyOwnInstallation()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(seed.CustomerUserA, Roles.Customer);

        var result = await client.GetJsonAsync<ProductionSummaryResponse>($"{Summary}?{Period}");

        Assert.Equal(1, result.InstallationCount);
        Assert.Equal(0.6, result.TotalEnergyKwh);
        Assert.Equal(1.0, result.PeakPowerKw);
    }

    [Fact]
    public async Task CustomerWithoutInstallations_GetsEmptySummary()
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(await NewUserAsync(), Roles.Customer);

        var result = await client.GetJsonAsync<ProductionSummaryResponse>($"{Summary}?{Period}");

        Assert.Equal(0, result.InstallationCount);
        Assert.Equal(0, result.TotalEnergyKwh);
        Assert.Null(result.PeakPowerKw);
        Assert.Empty(result.Points);
    }

    [Fact]
    public async Task PeriodWithoutData_GetsEmptyPoints()
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

        var result = await client.GetJsonAsync<ProductionSummaryResponse>($"{Summary}?from=2026-01-01&to=2026-01-02");

        Assert.Equal(2, result.InstallationCount);
        Assert.Equal(0, result.TotalEnergyKwh);
        Assert.Null(result.PeakPowerKw);
        Assert.Empty(result.Points);
    }

    [Fact]
    public async Task NoInstallations_GetsEmptySummary()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

        var result = await client.GetJsonAsync<ProductionSummaryResponse>($"{Summary}?{Period}");

        Assert.Equal(0, result.InstallationCount);
        Assert.Null(result.PeakPowerKw);
        Assert.Empty(result.Points);
    }

    [Theory]
    [InlineData("from=2026-06-21&to=2026-06-22&granularity=week")]
    [InlineData("from=2026-06-22&to=2026-06-21")]
    [InlineData("to=2026-06-22")]
    [InlineData("from=2026-01-01&to=2026-03-01&granularity=fiveMinutes")]
    [InlineData("from=2026-06-21&to=2026-06-22&timeZone=Nowhere/Land")]
    public async Task InvalidQuery_Returns400(string query)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

        using var response = await client.GetAsync($"{Summary}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync($"{Summary}?{Period}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_Returns403()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7());

        using var response = await client.GetAsync($"{Summary}?{Period}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
