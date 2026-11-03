using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SolarOps.Api.Features.Telemetry;

/// <summary>
/// Ingest batch of inverter measurements (contract of ADR #185, <c>schemaVersion</c> 1.0).
/// Status, source and timestamps are strings on purpose: an invalid value then yields a 400 that names the field
/// instead of a bare deserialization error. Validation of the nested readings is done here because the
/// DataAnnotations filter does not descend into collections; error keys look like <c>readings[3].timestamp</c>.
/// </summary>
public sealed partial class TelemetryBatchRequest : IValidatableObject
{
    public const string SupportedSchemaVersion = "1.0";
    public const int MaxReadings = 288;
    public const int MaxStrings = 12;
    public const int MaxIntervalSec = 3600;

    /// <summary>Tolerated clock skew: readings stamped further in the future are rejected.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static readonly IReadOnlyList<string> Sources = ["simulator", "gateway"];

    public string? SchemaVersion { get; init; }

    public Guid? InstallationId { get; init; }

    /// <summary>When the batch was sent, ISO 8601 UTC (suffix Z).</summary>
    public string? SentAt { get; init; }

    /// <summary><c>simulator</c> or <c>gateway</c>.</summary>
    public string? Source { get; init; }

    /// <summary>1 to 288 readings.</summary>
    public IReadOnlyList<TelemetryReadingRequest?>? Readings { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = new List<ValidationResult>();
        var now = (validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System).GetUtcNow();

        if (SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(Error(nameof(SchemaVersion), $"Unsupported schemaVersion; expected \"{SupportedSchemaVersion}\"."));
        }

        if (InstallationId is null || InstallationId == Guid.Empty)
        {
            errors.Add(Error(nameof(InstallationId), "installationId is required."));
        }

        if (!TelemetryParsing.TryParseUtc(SentAt, out _))
        {
            errors.Add(Error(nameof(SentAt), "sentAt is required, ISO 8601 UTC with suffix Z."));
        }

        if (Source is null || !Sources.Contains(Source))
        {
            errors.Add(Error(nameof(Source), $"source must be one of: {string.Join(", ", Sources)}."));
        }

        if (Readings is null || Readings.Count is 0 or > MaxReadings)
        {
            errors.Add(Error(nameof(Readings), $"readings must contain 1 to {MaxReadings} items."));
            return errors;
        }

        for (var i = 0; i < Readings.Count; i++)
        {
            var reading = Readings[i];
            if (reading is null)
            {
                errors.Add(Error($"readings[{i}]", "Reading must not be null."));
                continue;
            }

            reading.Validate(i, now, errors);
        }

        return errors;
    }

    internal static ValidationResult Error(string member, string message) => new(message, [member]);
}

/// <summary>One measurement of an inverter (<c>deviceId</c> is the id of an inverter registered in the installation).</summary>
public sealed class TelemetryReadingRequest
{
    public Guid? DeviceId { get; init; }

    /// <summary>End of the averaging window, UTC, 5-minute grid (minutes divisible by 5, seconds 0).</summary>
    public string? Timestamp { get; init; }

    /// <summary>Length of the averaging window in seconds (1 to 3600), 300 by default.</summary>
    public int? IntervalSec { get; init; }

    /// <summary>SunSpec state: OFF, SLEEPING, STARTING, MPPT, THROTTLED, SHUTTING_DOWN, FAULT, STANDBY.</summary>
    public string? Status { get; init; }

    public int? VendorStatusCode { get; init; }

    /// <summary>Active event names, e.g. GROUND_FAULT (A-Z, 0-9, underscore; unique).</summary>
    public IReadOnlyList<string?>? Events { get; init; }

    public double? AcPowerW { get; init; }

    public double? DcPowerW { get; init; }

    public double? AcVoltageV { get; init; }

    public double? AcCurrentA { get; init; }

    public double? GridFrequencyHz { get; init; }

    public double? TemperatureC { get; init; }

    /// <summary>Lifetime produced energy in Wh, monotonic counter.</summary>
    public double? TotalEnergyWh { get; init; }

    /// <summary>Optional device-reported energy since local midnight; informative only.</summary>
    public double? DayEnergyWh { get; init; }

    public IReadOnlyList<TelemetryStringRequest?>? Strings { get; init; }

    internal void Validate(int index, DateTimeOffset now, List<ValidationResult> errors)
    {
        var p = $"readings[{index}]";
        void Add(string field, string message) => errors.Add(TelemetryBatchRequest.Error($"{p}.{field}", message));

        if (DeviceId is null || DeviceId == Guid.Empty)
        {
            Add("deviceId", "deviceId is required.");
        }

        if (!TelemetryParsing.TryParseUtc(Timestamp, out var timestamp))
        {
            Add("timestamp", "timestamp is required, ISO 8601 UTC with suffix Z.");
        }
        else if (timestamp.Minute % 5 != 0 || timestamp.Second != 0 || timestamp.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            Add("timestamp", "timestamp must be aligned to the 5-minute UTC grid (minutes divisible by 5, seconds 0).");
        }
        else if (timestamp > now + TelemetryBatchRequest.FutureTolerance)
        {
            Add("timestamp", "timestamp must not be in the future.");
        }

        if (IntervalSec is null or < 1 or > TelemetryBatchRequest.MaxIntervalSec)
        {
            Add("intervalSec", $"intervalSec is required, 1 to {TelemetryBatchRequest.MaxIntervalSec}.");
        }

        if (!TelemetryParsing.TryParseStatus(Status, out _))
        {
            Add("status", $"status must be one of: {string.Join(", ", TelemetryParsing.StatusNames)}.");
        }

        if (Events is not null)
        {
            if (Events.Any(e => e is null || !TelemetryParsing.EventName().IsMatch(e)) || Events.Distinct().Count() != Events.Count)
            {
                Add("events", "events must be unique names matching ^[A-Z0-9_]+$.");
            }
        }

        if (AcPowerW is null)
        {
            Add("acPowerW", "acPowerW is required.");
        }

        if (TotalEnergyWh is null or < 0)
        {
            Add("totalEnergyWh", "totalEnergyWh is required and must not be negative.");
        }

        if (DayEnergyWh is < 0)
        {
            Add("dayEnergyWh", "dayEnergyWh must not be negative.");
        }

        if (Strings is not null)
        {
            if (Strings.Count > TelemetryBatchRequest.MaxStrings)
            {
                Add("strings", $"strings must contain at most {TelemetryBatchRequest.MaxStrings} items.");
            }

            for (var s = 0; s < Strings.Count; s++)
            {
                if (Strings[s] is not { Index: >= 1 })
                {
                    Add($"strings[{s}].index", "String index is required and must be at least 1.");
                }
            }
        }
    }
}

/// <summary>Values of one DC input (MPPT / string), SunSpec model 160.</summary>
public sealed class TelemetryStringRequest
{
    public int? Index { get; init; }

    public double? VoltageV { get; init; }

    public double? CurrentA { get; init; }

    public double? PowerW { get; init; }
}

/// <summary>Result of an ingest: how many readings were new and how many were duplicates (ignored).</summary>
public sealed record TelemetryIngestResponse(int Received, int Stored, int Duplicates);

internal static partial class TelemetryParsing
{
    private static readonly Dictionary<string, InverterStatus> Statuses = new(StringComparer.Ordinal)
    {
        ["OFF"] = InverterStatus.Off,
        ["SLEEPING"] = InverterStatus.Sleeping,
        ["STARTING"] = InverterStatus.Starting,
        ["MPPT"] = InverterStatus.Mppt,
        ["THROTTLED"] = InverterStatus.Throttled,
        ["SHUTTING_DOWN"] = InverterStatus.ShuttingDown,
        ["FAULT"] = InverterStatus.Fault,
        ["STANDBY"] = InverterStatus.Standby,
    };

    public static IEnumerable<string> StatusNames => Statuses.Keys;

    public static bool TryParseStatus(string? value, out InverterStatus status)
    {
        status = default;
        return value is not null && Statuses.TryGetValue(value, out status);
    }

    /// <summary>Parses ISO 8601 with the mandatory suffix Z (schema pattern); the result is UTC.</summary>
    public static bool TryParseUtc(string? value, out DateTimeOffset result)
    {
        result = default;
        if (value is null || !UtcTimestamp().IsMatch(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$")]
    private static partial Regex UtcTimestamp();

    [GeneratedRegex("^[A-Z0-9_]+$")]
    public static partial Regex EventName();
}
