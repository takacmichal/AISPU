namespace SolarOps.Api.Features.Telemetry;

/// <summary>
/// Inverter state as in SunSpec model 101/103 <c>St</c> (ADR #185). The numeric value is stored (smallint, ADR #220).
/// </summary>
public enum InverterStatus : short
{
    Off = 1,
    Sleeping = 2,
    Starting = 3,
    Mppt = 4,
    Throttled = 5,
    ShuttingDown = 6,
    Fault = 7,
    Standby = 8,
}

/// <summary>
/// One raw 5-minute measurement of an inverter (ADR #185, storage ADR #220). Identified by
/// <c>(device_id, ts)</c>, which is also the idempotency key of the ingest. Rows are written by the ingest
/// service with a single <c>INSERT ... ON CONFLICT DO NOTHING</c>; the class exists for the EF schema and reads.
/// There is no foreign key on purpose: the table is meant to be partitioned by <c>ts</c> later.
/// </summary>
public sealed class TelemetryReading
{
    public Guid DeviceId { get; private set; }

    /// <summary>End of the averaging window, UTC, on the 5-minute grid.</summary>
    public DateTime Timestamp { get; private set; }

    public int IntervalSec { get; private set; }

    public InverterStatus Status { get; private set; }

    public int? VendorStatusCode { get; private set; }

    /// <summary>JSON array of active event names (e.g. <c>["OVER_TEMP"]</c>).</summary>
    public string EventsJson { get; private set; } = "[]";

    public float AcPowerW { get; private set; }

    public float? DcPowerW { get; private set; }

    public float? VoltageV { get; private set; }

    public float? CurrentA { get; private set; }

    public float? FrequencyHz { get; private set; }

    public float? TemperatureC { get; private set; }

    public double? DayWh { get; private set; }

    public double TotalWh { get; private set; }

    /// <summary>JSON array of per-string (MPPT) values; <c>null</c> when the device did not report any.</summary>
    public string? StringsJson { get; private set; }

    /// <summary>Origin of the batch: <c>simulator</c> or <c>gateway</c>.</summary>
    public string Source { get; private set; } = string.Empty;

    public DateTime ReceivedAt { get; private set; }
}
