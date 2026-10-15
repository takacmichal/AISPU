using System.Text.RegularExpressions;

namespace SolarOps.Api.Features.Customers;

/// <summary>
/// Format and checksum validation of Slovak business identifiers.
/// All methods expect a value already normalized by <see cref="Normalize"/> (no whitespace, upper case).
/// </summary>
public static partial class SlovakIdentifiers
{
    /// <summary>Removes all whitespace and converts to upper case; returns null for blank input.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return WhitespaceRegex().Replace(value, string.Empty).ToUpperInvariant();
    }

    /// <summary>
    /// IČO (company registration number): 8 digits, the last one is a mod-11 check digit
    /// with weights 8..2 over the first seven digits: <c>check = (11 - sum % 11) % 10</c>.
    /// </summary>
    public static bool IsValidIco(string ico)
    {
        if (ico.Length != 8 || !ico.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            sum += (ico[i] - '0') * (8 - i);
        }

        var check = (11 - sum % 11) % 10;
        return ico[7] - '0' == check;
    }

    /// <summary>DIČ (tax identification number): 10 digits (format check only, no public checksum).</summary>
    public static bool IsValidDic(string dic) => dic.Length == 10 && dic.All(char.IsAsciiDigit);

    /// <summary>IČ DPH (VAT number): <c>SK</c> followed by 10 digits.</summary>
    public static bool IsValidIcDph(string icDph) => IcDphRegex().IsMatch(icDph);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^SK\d{10}$")]
    private static partial Regex IcDphRegex();
}
