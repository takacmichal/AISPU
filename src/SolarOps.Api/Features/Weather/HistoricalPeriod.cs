using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Weather;

/// <summary>
/// A range of whole UTC days in the past, both ends inclusive. At most <see cref="MaxDays"/> days per call; the last day
/// must be before today (UTC) because the archive is filled with a delay.
/// </summary>
public sealed record HistoricalPeriod(
    [property: Required(ErrorMessage = "From is required.")] DateOnly? From,
    [property: Required(ErrorMessage = "To is required.")] DateOnly? To) : IValidatableObject
{
    /// <summary>Longest period of one call.</summary>
    public const int MaxDays = 31;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is null || To is null)
        {
            yield break;
        }

        if (To < From)
        {
            yield return new ValidationResult("To must not be before From.", [nameof(To)]);
            yield break;
        }

        if (To.Value.DayNumber - From.Value.DayNumber + 1 > MaxDays)
        {
            yield return new ValidationResult($"The period may have at most {MaxDays} days.", [nameof(To)]);
        }

        var timeProvider = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (To.Value >= today)
        {
            yield return new ValidationResult("To must be before today (UTC); the archive is available only for past days.", [nameof(To)]);
        }
    }
}
