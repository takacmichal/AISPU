using System.Globalization;
using System.Text;

namespace SolarOps.Api.Features.Production;

/// <summary>
/// CSV of the production export (#102): UTF-8 without BOM (the content is ASCII only), CRLF line ends (RFC 4180),
/// a header row. With <see cref="CsvSeparator.Semicolon"/> numbers use a decimal comma, so Slovak Excel opens the
/// file as numbers; other separators use a decimal point.
/// </summary>
public static class ProductionCsv
{
    public const string ContentType = "text/csv; charset=utf-8";

    private const int FlushEveryRows = 500;

    private static readonly string[] Columns = ["period_start_utc", "period_start_local", "energy_kwh", "peak_power_kw", "sample_count"];

    private static readonly NumberFormatInfo DecimalComma = new() { NumberDecimalSeparator = ",", NegativeSign = "-" };

    public static string Header(CsvSeparator separator) => string.Join(Delimiter(separator), Columns);

    public static string Row(ProductionBucket bucket, CsvSeparator separator, TimeZoneInfo zone)
    {
        var numbers = separator == CsvSeparator.Semicolon ? DecimalComma : NumberFormatInfo.InvariantInfo;
        var utc = DateTime.SpecifyKind(bucket.PeriodStart, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);

        return string.Join(
            Delimiter(separator),
            utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Kilo(bucket.EnergyWh).ToString("0.###", numbers),
            Kilo(bucket.PeakPowerW).ToString("0.###", numbers),
            bucket.SampleCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes the header and the rows as they come from the database, flushing regularly.</summary>
    public static async Task WriteAsync(
        Stream stream, IAsyncEnumerable<ProductionBucket> rows, CsvSeparator separator, TimeZoneInfo zone, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        await writer.WriteLineAsync(Header(separator).AsMemory(), ct);
        var count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            await writer.WriteLineAsync(Row(row, separator, zone).AsMemory(), ct);
            if (++count % FlushEveryRows == 0)
            {
                await writer.FlushAsync(ct);
            }
        }

        await writer.FlushAsync(ct);
    }

    internal static double Kilo(double value) => Math.Round(value / 1000.0, 3, MidpointRounding.AwayFromZero);

    private static string Delimiter(CsvSeparator separator) => separator switch
    {
        CsvSeparator.Comma => ",",
        CsvSeparator.Tab => "\t",
        _ => ";",
    };
}
