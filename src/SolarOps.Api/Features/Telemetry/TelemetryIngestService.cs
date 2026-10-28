using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Telemetry;

public interface ITelemetryIngestService
{
    /// <summary>
    /// Stores a validated batch. Throws <see cref="NotFoundException"/> (404) for an unknown installation and
    /// <see cref="RequestValidationException"/> (400) when a reading refers to a device that is not an inverter of
    /// the installation; in both cases nothing is stored. Readings that already exist are ignored.
    /// </summary>
    Task<TelemetryIngestResponse> IngestAsync(TelemetryBatchRequest request, CancellationToken ct);
}

/// <summary>Batch ingest into <c>telemetry_readings</c> (#95, ADR #185/#220).</summary>
public sealed class TelemetryIngestService(AppDbContext db, TimeProvider time) : ITelemetryIngestService
{
    /// <summary>JSON stored in the jsonb columns: camelCase names as in the contract, without nulls.</summary>
    private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<TelemetryIngestResponse> IngestAsync(TelemetryBatchRequest request, CancellationToken ct)
    {
        var installationId = request.InstallationId!.Value;
        var readings = request.Readings!;

        if (!await db.Set<Installation>().AnyAsync(x => x.Id == installationId, ct))
        {
            throw new NotFoundException($"Installation '{installationId}' was not found.");
        }

        await EnsureDevicesAsync(installationId, readings!, ct);

        var n = readings.Count;
        var deviceIds = new Guid[n];
        var timestamps = new DateTime[n];
        var intervals = new int[n];
        var statuses = new short[n];
        var vendorCodes = new int?[n];
        var events = new string[n];
        var acPower = new float[n];
        var dcPower = new float?[n];
        var voltage = new float?[n];
        var current = new float?[n];
        var frequency = new float?[n];
        var temperature = new float?[n];
        var dayWh = new double?[n];
        var totalWh = new double[n];
        var strings = new string?[n];

        for (var i = 0; i < n; i++)
        {
            var r = readings[i]!;
            TelemetryParsing.TryParseUtc(r.Timestamp, out var ts);
            TelemetryParsing.TryParseStatus(r.Status, out var status);

            deviceIds[i] = r.DeviceId!.Value;
            timestamps[i] = ts.UtcDateTime;
            intervals[i] = r.IntervalSec!.Value;
            statuses[i] = (short)status;
            vendorCodes[i] = r.VendorStatusCode;
            events[i] = JsonSerializer.Serialize(r.Events ?? [], StorageJson);
            acPower[i] = (float)r.AcPowerW!.Value;
            dcPower[i] = ToSingle(r.DcPowerW);
            voltage[i] = ToSingle(r.AcVoltageV);
            current[i] = ToSingle(r.AcCurrentA);
            frequency[i] = ToSingle(r.GridFrequencyHz);
            temperature[i] = ToSingle(r.TemperatureC);
            dayWh[i] = r.DayEnergyWh;
            totalWh[i] = r.TotalEnergyWh!.Value;
            strings[i] = r.Strings is { Count: > 0 } ? JsonSerializer.Serialize(r.Strings, StorageJson) : null;
        }

        var source = request.Source!;
        var receivedAt = time.GetUtcNow().UtcDateTime;

        // One statement: duplicates (also inside the batch and under concurrent requests) are skipped by the primary key.
        var stored = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO telemetry_readings (device_id, ts, interval_sec, status, vendor_status_code, events,
                ac_power_w, dc_power_w, voltage_v, current_a, frequency_hz, temperature_c,
                day_wh, total_wh, strings, source, received_at)
            SELECT r.device_id, r.ts, r.interval_sec, r.status, r.vendor_status_code, r.events::jsonb,
                r.ac_power_w, r.dc_power_w, r.voltage_v, r.current_a, r.frequency_hz, r.temperature_c,
                r.day_wh, r.total_wh, r.strings::jsonb, {source}, {receivedAt}
            FROM unnest(
                {deviceIds}::uuid[], {timestamps}::timestamptz[], {intervals}::integer[], {statuses}::smallint[],
                {vendorCodes}::integer[], {events}::text[], {acPower}::real[], {dcPower}::real[], {voltage}::real[],
                {current}::real[], {frequency}::real[], {temperature}::real[], {dayWh}::double precision[],
                {totalWh}::double precision[], {strings}::text[])
                AS r(device_id, ts, interval_sec, status, vendor_status_code, events,
                    ac_power_w, dc_power_w, voltage_v, current_a, frequency_hz, temperature_c,
                    day_wh, total_wh, strings)
            ON CONFLICT (device_id, ts) DO NOTHING
            """, ct);

        return new TelemetryIngestResponse(n, stored, n - stored);
    }

    /// <summary>
    /// One query for all devices of the batch. A replaced inverter is accepted on purpose, so that a gateway can
    /// deliver the backlog it measured before the replacement.
    /// </summary>
    private async Task EnsureDevicesAsync(Guid installationId, IReadOnlyList<TelemetryReadingRequest?> readings, CancellationToken ct)
    {
        var requested = readings.Select(r => r!.DeviceId!.Value).Distinct().ToArray();
        var known = await db.Set<InstallationDevice>()
            .AsNoTracking()
            .Where(d => d.InstallationId == installationId && d.Type == DeviceType.Inverter && requested.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);

        var knownSet = known.ToHashSet();
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < readings.Count; i++)
        {
            if (!knownSet.Contains(readings[i]!.DeviceId!.Value))
            {
                errors[$"readings[{i}].deviceId"] = [$"Device '{readings[i]!.DeviceId}' is not an inverter of installation '{installationId}'."];
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }

    private static float? ToSingle(double? value) => value is null ? null : (float)value.Value;
}
