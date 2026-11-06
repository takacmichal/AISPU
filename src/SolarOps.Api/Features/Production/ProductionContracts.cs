using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Production;

/// <summary>Size of one aggregation bucket.</summary>
public enum ProductionGranularity
{
    FiveMinutes,
    Hour,
    Day,
    Month,
}

/// <summary>Field separator of the CSV export.</summary>
public enum CsvSeparator
{
    Semicolon,
    Comma,
    Tab,
}

/// <summary>Validated period: UTC bounds <c>[From, To)</c>, bucket size and the IANA time zone of day and month buckets.</summary>
public sealed record ProductionPeriod(DateTimeOffset From, DateTimeOffset To, ProductionGranularity Granularity, string TimeZoneId)
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}

/// <summary>
/// Query string of the production endpoints (#100). Values are strings on purpose: an invalid value yields a 400
/// naming the parameter instead of a bare binding error. <c>from</c> and <c>to</c> are ISO 8601 with <c>Z</c> or an
/// offset, or a date (<c>2026-06-01</c>) meaning local midnight in <c>timeZone</c>.
/// </summary>
public partial class ProductionQuery : IValidatableObject
{
    /// <summary>Day and month buckets follow the local calendar of the company (ADR #220: the API resolves the local day).</summary>
    public const string DefaultTimeZone = "Europe/Bratislava";

    public const ProductionGranularity DefaultGranularity = ProductionGranularity.Hour;

    /// <summary>Start of the period (inclusive).</summary>
    public string? From { get; init; }

    /// <summary>End of the period (exclusive).</summary>
    public string? To { get; init; }

    /// <summary><c>fiveMinutes</c>, <c>hour</c> (default), <c>day</c> or <c>month</c>.</summary>
    public string? Granularity { get; init; }

    /// <summary>IANA time zone for day and month buckets and date-only bounds; default Europe/Bratislava.</summary>
    public string? TimeZone { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        TryResolve(out _, out var errors);
        return errors.Select(e => new ValidationResult(e.Value, [e.Key]));
    }

    /// <summary>Returns the validated period or throws <see cref="RequestValidationException"/> (400).</summary>
    public ProductionPeriod ToPeriod() =>
        TryResolve(out var period, out var errors)
            ? period!
            : throw new RequestValidationException(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    /// <summary>Longest period for the granularity; keeps one response at most about 9 000 points.</summary>
    protected virtual TimeSpan MaxRange(ProductionGranularity granularity) => granularity switch
    {
        ProductionGranularity.FiveMinutes => TimeSpan.FromDays(31),
        ProductionGranularity.Hour => TimeSpan.FromDays(366),
        _ => TimeSpan.FromDays(3660),
    };

    protected virtual bool IsAllowed(ProductionGranularity granularity) => true;

    protected virtual void ValidateMore(Dictionary<string, string> errors)
    {
    }

    private bool TryResolve(out ProductionPeriod? period, out Dictionary<string, string> errors)
    {
        period = null;
        errors = new Dictionary<string, string>(StringComparer.Ordinal);

        var granularity = DefaultGranularity;
        var knownGranularity = string.IsNullOrWhiteSpace(Granularity)
            || (!int.TryParse(Granularity, out _) && Enum.TryParse(Granularity, ignoreCase: true, out granularity) && Enum.IsDefined(granularity));
        if (!knownGranularity || !IsAllowed(granularity))
        {
            errors["granularity"] = $"granularity must be one of: {string.Join(", ", AllowedGranularities())}.";
        }

        var timeZoneId = string.IsNullOrWhiteSpace(TimeZone) ? DefaultTimeZone : TimeZone.Trim();
        // IANA only: PostgreSQL resolves the same id for date_trunc, Windows ids would fail there.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) || !zone.HasIanaId)
        {
            errors["timeZone"] = "timeZone must be an IANA time zone, e.g. Europe/Bratislava or UTC.";
            zone = null;
        }

        var from = ParseBound(From, "from", zone, errors);
        var to = ParseBound(To, "to", zone, errors);

        if (from is not null && to is not null)
        {
            if (to <= from)
            {
                errors["to"] = "to must be after from.";
            }
            else if (!errors.ContainsKey("granularity") && to - from > MaxRange(granularity))
            {
                errors["to"] = $"The period is too long for granularity {granularity}: at most {MaxRange(granularity).TotalDays:0} days.";
            }
        }

        ValidateMore(errors);

        if (errors.Count > 0)
        {
            return false;
        }

        period = new ProductionPeriod(from!.Value, to!.Value, granularity, timeZoneId);
        return true;
    }

    private IEnumerable<string> AllowedGranularities() =>
        Enum.GetValues<ProductionGranularity>().Where(IsAllowed).Select(g => JsonNamingPolicy.CamelCase.ConvertName(g.ToString()));

    private static DateTimeOffset? ParseBound(string? value, string key, TimeZoneInfo? zone, Dictionary<string, string> errors)
    {
        const string Message = " must be an ISO 8601 timestamp with Z or an offset (2026-06-21T00:00:00Z) or a date (2026-06-21).";
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = $"{key} is required; it{Message}";
            return null;
        }

        if (DateOnlyPattern().IsMatch(value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            if (zone is null)
            {
                return null; // the time zone error is reported already
            }

            var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // A DST gap at midnight (not in Europe) moves the start to the first valid local hour.
            while (zone.IsInvalidTime(midnight))
            {
                midnight = midnight.AddHours(1);
            }

            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone), TimeSpan.Zero);
        }

        if (TimestampPattern().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            return timestamp.ToUniversalTime();
        }

        errors[key] = key + Message;
        return null;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DateOnlyPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex TimestampPattern();
}

/// <summary>Query of the comparison with the prediction (#101): predictions are hourly (ADR #196), so no 5-minute buckets.</summary>
public sealed class ProductionComparisonQuery : ProductionQuery
{
    protected override bool IsAllowed(ProductionGranularity granularity) => granularity != ProductionGranularity.FiveMinutes;
}

/// <summary>Query of the CSV export (#102): longer periods are allowed because the rows are streamed.</summary>
public sealed class ProductionExportQuery : ProductionQuery
{
    /// <summary><c>semicolon</c> (default, decimal comma for Slovak Excel), <c>comma</c> or <c>tab</c> (decimal point).</summary>
    public string? Separator { get; init; }

    public CsvSeparator ToSeparator() =>
        TryParseSeparator(out var separator)
            ? separator
            : throw new RequestValidationException("separator", SeparatorMessage);

    protected override TimeSpan MaxRange(ProductionGranularity granularity) =>
        granularity == ProductionGranularity.FiveMinutes ? TimeSpan.FromDays(366) : TimeSpan.FromDays(3660);

    protected override void ValidateMore(Dictionary<string, string> errors)
    {
        if (!TryParseSeparator(out _))
        {
            errors["separator"] = SeparatorMessage;
        }
    }

    private const string SeparatorMessage = "separator must be one of: semicolon, comma, tab.";

    private bool TryParseSeparator(out CsvSeparator separator)
    {
        separator = CsvSeparator.Semicolon;
        return string.IsNullOrWhiteSpace(Separator)
            || (!int.TryParse(Separator, out _) && Enum.TryParse(Separator, ignoreCase: true, out separator) && Enum.IsDefined(separator));
    }
}

/// <summary>Production of an installation for a period (#100). Points exist only for buckets with data.</summary>
/// <param name="InstallationId">Installation the production belongs to.</param>
/// <param name="From">Start of the period, UTC (inclusive).</param>
/// <param name="To">End of the period, UTC (exclusive).</param>
/// <param name="Granularity">Bucket size.</param>
/// <param name="TimeZone">IANA time zone of day and month buckets.</param>
/// <param name="TotalEnergyKwh">Sum of the energy of all points.</param>
/// <param name="PeakPowerKw">Highest installation AC power (sum of its inverters) in the period; null without data.</param>
/// <param name="Points">Buckets with data, oldest first.</param>
public sealed record ProductionResponse(
    Guid InstallationId,
    DateTimeOffset From,
    DateTimeOffset To,
    ProductionGranularity Granularity,
    string TimeZone,
    double TotalEnergyKwh,
    double? PeakPowerKw,
    IReadOnlyList<ProductionPointResponse> Points);

/// <summary>Production of all installations visible to the caller for a period. Points exist only for buckets with data.</summary>
/// <param name="From">Start of the period, UTC (inclusive).</param>
/// <param name="To">End of the period, UTC (exclusive).</param>
/// <param name="Granularity">Bucket size.</param>
/// <param name="TimeZone">IANA time zone of day and month buckets.</param>
/// <param name="InstallationCount">Number of installations the caller may see (also those without data).</param>
/// <param name="TotalEnergyKwh">Sum of the energy of all points.</param>
/// <param name="PeakPowerKw">Highest summed AC power of all visible installations in the period; null without data.</param>
/// <param name="Points">Buckets with data, oldest first; power and energy are summed over all visible installations.</param>
public sealed record ProductionSummaryResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    ProductionGranularity Granularity,
    string TimeZone,
    int InstallationCount,
    double TotalEnergyKwh,
    double? PeakPowerKw,
    IReadOnlyList<ProductionPointResponse> Points);

/// <param name="PeriodStart">Start of the bucket (UTC instant; day and month buckets start at local midnight).</param>
/// <param name="EnergyKwh">Energy fed in by the inverters in the bucket (from the totalWh counters).</param>
/// <param name="PeakPowerKw">Highest AC power of the installation in the bucket.</param>
/// <param name="SampleCount">Number of 5-minute (measurement) windows with data; shows gaps in the data.</param>
public sealed record ProductionPointResponse(DateTimeOffset PeriodStart, double EnergyKwh, double PeakPowerKw, int SampleCount);

/// <summary>Actual vs. predicted production (#101).</summary>
/// <param name="InstallationId">Installation the production belongs to.</param>
/// <param name="From">Start of the period, UTC (inclusive).</param>
/// <param name="To">End of the period, UTC (exclusive).</param>
/// <param name="Granularity">Bucket size.</param>
/// <param name="TimeZone">IANA time zone of day and month buckets.</param>
/// <param name="PredictionAvailable">False when there is no prediction for the period (no model or no stored prediction yet).</param>
/// <param name="ModelVersion">Version of the model that produced the prediction; null without a prediction.</param>
/// <param name="ActualEnergyKwh">Measured energy in the period.</param>
/// <param name="PredictedEnergyKwh">Predicted energy in the period; null without a prediction.</param>
/// <param name="Points">Buckets with a measurement or a prediction, oldest first.</param>
public sealed record ProductionComparisonResponse(
    Guid InstallationId,
    DateTimeOffset From,
    DateTimeOffset To,
    ProductionGranularity Granularity,
    string TimeZone,
    bool PredictionAvailable,
    string? ModelVersion,
    double ActualEnergyKwh,
    double? PredictedEnergyKwh,
    IReadOnlyList<ProductionComparisonPointResponse> Points);

/// <param name="PeriodStart">Start of the bucket (UTC instant).</param>
/// <param name="ActualEnergyKwh">Null when there is no measurement in the bucket (missing data is not zero production).</param>
/// <param name="PredictedEnergyKwh">Null when there is no prediction for the bucket.</param>
/// <param name="DeviationKwh">Actual minus predicted; null when one of them is missing.</param>
/// <param name="Ratio">Actual / predicted (ADR #194 compares it with 0.85); null when one is missing or the prediction is 0.</param>
public sealed record ProductionComparisonPointResponse(
    DateTimeOffset PeriodStart,
    double? ActualEnergyKwh,
    double? PredictedEnergyKwh,
    double? DeviationKwh,
    double? Ratio);

/// <summary>One aggregated bucket as returned by the SQL query (snake_case columns).</summary>
public sealed record ProductionBucket(DateTime PeriodStart, double EnergyWh, double PeakPowerW, int SampleCount);
