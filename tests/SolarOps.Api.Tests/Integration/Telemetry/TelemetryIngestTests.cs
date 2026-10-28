using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Telemetry;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.DeviceKeys;
using InstallationsTestData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Telemetry;

[Collection(PostgresCollection.Name)]
public sealed class TelemetryIngestTests(PostgresFixture postgres) : IDisposable
{
    private const string Url = "/api/telemetry";
    private static readonly DateTimeOffset T0 = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Operator() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    /// <summary>Client authenticated only by the API key of <paramref name="installationId"/>.</summary>
    private async Task<HttpClient> DeviceAsync(Guid installationId) =>
        _factory.CreateDeviceClient((await _factory.IssueKeyAsync(installationId)).ApiKey);

    private async Task<(Guid InstallationId, Guid InverterId)> SeedAsync(string serial = "INV-0001")
    {
        using var op = Operator();
        var installation = (await InstallationsTestData.CreateInstallationAsync(op)).Installation.Id;
        using var response = await op.PostJsonAsync($"/api/installations/{installation}/devices", new AddDeviceRequest
        {
            Type = DeviceType.Inverter,
            SerialNumber = serial,
            Manufacturer = "Fronius",
            Model = "Symo 10.0",
            PowerW = 10000,
        });
        response.EnsureSuccessStatusCode();
        return (installation, (await response.ReadJsonAsync<DeviceResponse>())!.Id);
    }

    private static object Reading(Guid deviceId, int slot = 0, string? timestamp = null, string status = "MPPT") => new
    {
        deviceId,
        timestamp = timestamp ?? T0.AddMinutes(5 * slot).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        intervalSec = 300,
        status,
        acPowerW = 7420.5 + slot,
        dcPowerW = 7615.0,
        totalEnergyWh = 18452310.0 + slot,
        events = new[] { "OVER_TEMP" },
        strings = new[] { new { index = 1, voltageV = 612.3, currentA = 6.31, powerW = 3863.5 } },
    };

    private static Dictionary<string, object?> Batch(Guid installationId, params object[] readings) => new()
    {
        ["schemaVersion"] = "1.0",
        ["installationId"] = installationId,
        ["sentAt"] = "2026-06-21T10:36:00Z",
        ["source"] = "simulator",
        ["readings"] = readings,
    };

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response) =>
        await response.ReadJsonAsync<JsonElement>();

    private Task<int> CountRowsAsync() => _factory.ExecuteDbAsync(db => db.Set<TelemetryReading>().CountAsync());

    [Fact]
    public async Task Accepts_a_new_batch_and_stores_readings()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 0), Reading(inverter, 1)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = (await response.ReadJsonAsync<TelemetryIngestResponse>())!;
        Assert.Equal(new TelemetryIngestResponse(2, 2, 0), body);

        var rows = await _factory.ExecuteDbAsync(db => db.Set<TelemetryReading>().OrderBy(r => r.Timestamp).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.Equal(inverter, rows[0].DeviceId);
        Assert.Equal(T0.UtcDateTime, rows[0].Timestamp.ToUniversalTime());
        Assert.Equal(InverterStatus.Mppt, rows[0].Status);
        Assert.Equal(300, rows[0].IntervalSec);
        Assert.Equal(7420.5f, rows[0].AcPowerW);
        Assert.Equal(7615.0f, rows[0].DcPowerW);
        Assert.Null(rows[0].VoltageV);
        Assert.Equal(18452310.0, rows[0].TotalWh);
        Assert.Equal("simulator", rows[0].Source);
        Assert.Contains("OVER_TEMP", rows[0].EventsJson);
        Assert.Contains("\"voltageV\"", rows[0].StringsJson);
    }

    [Fact]
    public async Task Resending_the_same_batch_is_idempotent()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);
        var batch = Batch(installation, Reading(inverter, 0), Reading(inverter, 1));

        (await client.PostJsonAsync(Url, batch)).EnsureSuccessStatusCode();
        using var again = await client.PostJsonAsync(Url, batch);

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(new TelemetryIngestResponse(2, 0, 2), await again.ReadJsonAsync<TelemetryIngestResponse>());
        Assert.Equal(2, await CountRowsAsync());
    }

    [Fact]
    public async Task Overlapping_batch_stores_only_new_readings()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);
        (await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 0), Reading(inverter, 1)))).EnsureSuccessStatusCode();

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 1), Reading(inverter, 2)));

        Assert.Equal(new TelemetryIngestResponse(2, 1, 1), await response.ReadJsonAsync<TelemetryIngestResponse>());
        Assert.Equal(3, await CountRowsAsync());
    }

    [Fact]
    public async Task Duplicates_inside_one_batch_are_stored_once()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 0), Reading(inverter, 0)));

        Assert.Equal(new TelemetryIngestResponse(2, 1, 1), await response.ReadJsonAsync<TelemetryIngestResponse>());
        Assert.Equal(1, await CountRowsAsync());
    }

    [Theory]
    [InlineData("2026-06-21T10:33:00Z", "readings[0].timestamp")] // off the 5-minute grid
    [InlineData("2026-06-21T10:35:30Z", "readings[0].timestamp")] // seconds
    [InlineData("2026-06-21T12:35:00+02:00", "readings[0].timestamp")] // not UTC
    [InlineData("2026-06-21T10:35:00", "readings[0].timestamp")] // no zone
    [InlineData("yesterday", "readings[0].timestamp")]
    public async Task Rejects_invalid_timestamp_with_detail(string timestamp, string key)
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 0, timestamp)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(key, out _));
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task Rejects_future_timestamp()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var timestamp = new DateTimeOffset(future.Year, future.Month, future.Day, future.Hour, 0, 0, TimeSpan.Zero).ToString("yyyy-MM-ddTHH:mm:ssZ");

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 0, timestamp)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task One_invalid_reading_rejects_the_whole_batch_and_names_it()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter, 0), Reading(inverter, 1, status: "BROKEN")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("readings[1].status", out _));
        Assert.False(errors.TryGetProperty("readings[0].status", out _));
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task Rejects_missing_required_fields_and_wrong_envelope()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);
        var body = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "2.0",
            ["installationId"] = installation,
            ["source"] = "toaster",
            ["readings"] = new object[] { new { deviceId = inverter, timestamp = "2026-06-21T10:35:00Z", intervalSec = 0, status = "MPPT", totalEnergyWh = -1 } },
        };

        using var response = await client.PostJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        foreach (var key in new[] { "SchemaVersion", "SentAt", "Source", "readings[0].intervalSec", "readings[0].acPowerW", "readings[0].totalEnergyWh" })
        {
            Assert.True(errors.TryGetProperty(key, out _), key);
        }
    }

    [Fact]
    public async Task Rejects_empty_and_oversized_batches()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);

        using var empty = await client.PostJsonAsync(Url, Batch(installation));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooMany = Enumerable.Range(0, TelemetryBatchRequest.MaxReadings + 1).Select(i => Reading(inverter, i)).ToArray();
        using var big = await client.PostJsonAsync(Url, Batch(installation, tooMany));
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task Accepts_a_full_day_batch()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = await DeviceAsync(installation);
        var readings = Enumerable.Range(0, TelemetryBatchRequest.MaxReadings).Select(i => Reading(inverter, i)).ToArray();

        using var response = await client.PostJsonAsync(Url, Batch(installation, readings));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(TelemetryBatchRequest.MaxReadings, await CountRowsAsync());
    }

    [Fact]
    public async Task Rejects_device_from_another_installation()
    {
        var (installation, _) = await SeedAsync("INV-0001");
        var (_, foreignInverter) = await SeedAsync("INV-0002");
        using var client = await DeviceAsync(installation);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(foreignInverter)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty("readings[0].deviceId", out _));
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task Rejects_unknown_device_and_panel()
    {
        var (installation, _) = await SeedAsync();
        using var op = Operator();
        using var device = await DeviceAsync(installation);
        using var panelResponse = await op.PostJsonAsync($"/api/installations/{installation}/devices", new AddDeviceRequest
        {
            Type = DeviceType.Panel,
            SerialNumber = "PAN-1",
            Manufacturer = "JA Solar",
            Model = "JAM54S30",
            PowerW = 450,
        });
        var panel = (await panelResponse.ReadJsonAsync<DeviceResponse>())!.Id;

        using var unknown = await device.PostJsonAsync(Url, Batch(installation, Reading(Guid.CreateVersion7())));
        using var asPanel = await device.PostJsonAsync(Url, Batch(installation, Reading(panel)));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, asPanel.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var (installation, inverter) = await SeedAsync();
        using var client = _factory.CreateApiClient();

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("sok_nonsense")]
    [InlineData("not-a-key")]
    [InlineData("sok_00000000000000000000000000000000_AAAA")] // well formed, unknown id
    public async Task Unknown_or_malformed_key_is_401(string apiKey)
    {
        var (installation, inverter) = await SeedAsync();
        using var client = _factory.CreateDeviceClient(apiKey);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task Key_with_wrong_secret_is_401()
    {
        var (installation, inverter) = await SeedAsync();
        var issued = await _factory.IssueKeyAsync(installation);
        var forged = issued.ApiKey[..^4] + "AAAA";
        using var client = _factory.CreateDeviceClient(forged);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Key_of_another_installation_is_403()
    {
        var (installation, _) = await SeedAsync("INV-0001");
        var (otherInstallation, otherInverter) = await SeedAsync("INV-0002");
        using var client = await DeviceAsync(installation);

        using var response = await client.PostJsonAsync(Url, Batch(otherInstallation, Reading(otherInverter)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountRowsAsync());
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Customer)]
    public async Task Logged_in_user_without_a_key_is_401(string role)
    {
        var (installation, inverter) = await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.PostJsonAsync(Url, Batch(installation, Reading(inverter)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
