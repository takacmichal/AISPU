namespace SolarOps.Api.Features.Telemetry;

/// <summary>
/// Inverter status of a reading, mirroring the SunSpec <c>St</c> values of the telemetry contract (ADR-185).
/// Names match the JSON values on purpose.
/// </summary>
public enum TelemetryStatus
{
    OFF,
    SLEEPING,
    STARTING,
    MPPT,
    THROTTLED,
    SHUTTING_DOWN,
    FAULT,
    STANDBY,
}

/// <summary>
/// One raw inverter sample (5-minute interval by default) stored in <c>telemetry_readings</c> (ADR-220).
/// The primary key <c>(device_id, ts)</c> makes ingestion idempotent: a repeated sample is rejected by the database.
/// The denormalized <c>installation_id</c> and its index keep per-installation range queries fast without a join.
/// </summary>
public sealed class TelemetryReading
{
    public const short DefaultIntervalSeconds = 300;

    private TelemetryReading()
    {
    }

    public Guid DeviceId { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>Sample time in UTC (stored as <c>timestamptz</c> in the <c>ts</c> column).</summary>
    public DateTimeOffset Timestamp { get; private set; }

    public Guid MessageId { get; private set; }

    public short IntervalSeconds { get; private set; }

    public TelemetryStatus Status { get; private set; }

    public float? AcPowerW { get; private set; }

    public float? DcPowerW { get; private set; }

    public float? AcVoltageV { get; private set; }

    public float? AcCurrentA { get; private set; }

    public float? FrequencyHz { get; private set; }

    public float? PowerFactor { get; private set; }

    public float? TemperatureC { get; private set; }

    public double? DayWh { get; private set; }

    public double? TotalWh { get; private set; }

    public int ErrorCode { get; private set; }

    public static TelemetryReading Create(
        Guid installationId,
        Guid deviceId,
        DateTimeOffset timestamp,
        TelemetryStatus status,
        Guid messageId,
        int errorCode = 0,
        short intervalSeconds = DefaultIntervalSeconds,
        float? acPowerW = null,
        float? dcPowerW = null,
        float? acVoltageV = null,
        float? acCurrentA = null,
        float? frequencyHz = null,
        float? powerFactor = null,
        float? temperatureC = null,
        double? dayWh = null,
        double? totalWh = null) => new()
    {
        DeviceId = deviceId,
        InstallationId = installationId,
        // Npgsql requires UTC offsets for timestamptz values.
        Timestamp = timestamp.ToUniversalTime(),
        MessageId = messageId,
        IntervalSeconds = intervalSeconds,
        Status = status,
        AcPowerW = acPowerW,
        DcPowerW = dcPowerW,
        AcVoltageV = acVoltageV,
        AcCurrentA = acCurrentA,
        FrequencyHz = frequencyHz,
        PowerFactor = powerFactor,
        TemperatureC = temperatureC,
        DayWh = dayWh,
        TotalWh = totalWh,
        ErrorCode = errorCode,
    };
}
