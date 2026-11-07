using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Infrastructure.Querying;

/// <summary>Parsing of enum filters given as text in the query string.</summary>
public static class EnumFilter
{
    /// <summary>
    /// Parses a filter value by name (case-insensitive). Null or blank means "no filter"; an unknown name or a number
    /// that is not a defined value (for example <c>99</c>) is a 400 on <paramref name="field"/>.
    /// </summary>
    public static TEnum? Parse<TEnum>(string? value, string field) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new RequestValidationException(field, $"Unknown value '{value}'. Allowed: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }
}
