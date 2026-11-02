using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Telemetry;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Telemetry;

[Collection(PostgresCollection.Name)]
public sealed class TelemetryStorageTests(PostgresFixture postgres) : IDisposable
{
    private const int Devices = 100;
    private const int SamplesPerDevice = 10_000;
    private const int Rows = Devices * SamplesPerDevice;
    private const int SamplesInThirtyDays = 30 * 24 * 12;

    private static readonly DateTimeOffset SeedStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string SeedSql = """
        INSERT INTO telemetry_readings
            (device_id, installation_id, ts, message_id, interval_seconds, status,
             ac_power_w, dc_power_w, ac_voltage_v, ac_current_a, frequency_hz,
             power_factor, temperature_c, day_wh, total_wh, error_code)
        SELECT
            md5('device-' || device_no)::uuid,
            md5('installation-' || device_no)::uuid,
            TIMESTAMPTZ '2026-01-01 00:00:00+00' + sample_no * INTERVAL '5 minutes',
            md5('message-' || sample_no)::uuid,
            300,
            'MPPT',
            (1000 + sample_no % 5000)::real,
            (1100 + sample_no % 5000)::real,
            231.4::real,
            10.7::real,
            50.01::real,
            0.99::real,
            46.2::real,
            31250.0::double precision,
            18452310.0::double precision,
            0
        FROM generate_series(0, 999999) AS g(sample_no)
        CROSS JOIN LATERAL (SELECT sample_no / 10000 + 1 AS device_no) AS d
        """;

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task DuplicateReading_IsRejectedByPrimaryKey()
    {
        var installationId = Guid.CreateVersion7();
        var deviceId = Guid.CreateVersion7();
        var timestamp = new DateTimeOffset(2026, 6, 21, 10, 35, 0, TimeSpan.Zero);

        await _factory.ExecuteDbAsync(async db =>
        {
            db.Add(TelemetryReading.Create(
                installationId, deviceId, timestamp, TelemetryStatus.MPPT, Guid.CreateVersion7(), acPowerW: 7420.5f));
            await db.SaveChangesAsync();
        });

        // A separate DbContext, so the duplicate reaches the database instead of being caught by the identity map.
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            _factory.ExecuteDbAsync(async db =>
            {
                db.Add(TelemetryReading.Create(
                    installationId, deviceId, timestamp, TelemetryStatus.MPPT, Guid.CreateVersion7(), acPowerW: 7420.5f));
                await db.SaveChangesAsync();
            }));

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    [Fact]
    public async Task Table_HasInstallationTimeAndBrinIndexes()
    {
        var indexNames = await _factory.ExecuteDbAsync(db => db.Database
            .SqlQuery<string>(
                $"SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'telemetry_readings'")
            .ToListAsync());

        Assert.Contains(TelemetryReadingConfiguration.InstallationTimeIndexName, indexNames);
        Assert.Contains(TelemetryReadingConfiguration.TimeBrinIndexName, indexNames);
        Assert.Contains("pk_telemetry_readings", indexNames);

        var indexDefinition = await _factory.ExecuteDbAsync(db => db.Database
            .SqlQuery<string>(
                $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND indexname = {TelemetryReadingConfiguration.InstallationTimeIndexName}")
            .SingleAsync());
        Assert.Contains("INCLUDE", indexDefinition);
        Assert.Contains("ac_power_w", indexDefinition);
    }

    [Fact]
    public async Task Database_IsUpToDateWithCommittedMigrations()
    {
        var applied = await _factory.ExecuteDbAsync(db => db.Database.GetAppliedMigrationsAsync());
        var pending = await _factory.ExecuteDbAsync(db => db.Database.GetPendingMigrationsAsync());

        Assert.Contains(applied, name => name.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Empty(pending);
    }

    [Fact]
    public async Task ThirtyDayQueryForOneInstallation_IsUnder200msOnOneMillionRows()
    {
        await _factory.ExecuteDbAsync(db => db.Database.ExecuteSqlRawAsync(SeedSql));
        // VACUUM marks the heap pages as all-visible, so the covering index can answer the query alone.
        await _factory.ExecuteDbAsync(db => db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) telemetry_readings"));

        var rowCount = await _factory.ExecuteDbAsync(db => db.Set<TelemetryReading>().CountAsync());
        Assert.Equal(Rows, rowCount);

        var installationId = await _factory.ExecuteDbAsync(db => db.Set<TelemetryReading>()
            .Select(x => x.InstallationId)
            .FirstAsync());

        var to = SeedStart.AddDays(30);
        Task<List<Reading>> Query() => _factory.ExecuteDbAsync(db => db.Set<TelemetryReading>()
            .AsNoTracking()
            .Where(x => x.InstallationId == installationId && x.Timestamp >= SeedStart && x.Timestamp < to)
            .Select(x => new Reading(x.Timestamp, x.AcPowerW))
            .ToListAsync());

        // Warm up EF query compilation and the provider before measuring the query itself.
        Assert.Equal(SamplesInThirtyDays, (await Query()).Count);

        var measurements = new List<double>(3);
        for (var i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var readings = await Query();
            stopwatch.Stop();

            Assert.Equal(SamplesInThirtyDays, readings.Count);
            measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var median = measurements.Order().ElementAt(measurements.Count / 2);
        Assert.True(median < 200,
            $"30-day query median took {median:F0} ms on {Rows:N0} rows (runs: {string.Join(", ", measurements.Select(x => $"{x:F0} ms"))}).");
    }

    private sealed record Reading(DateTimeOffset Timestamp, float? AcPowerW);
}
