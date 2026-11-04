using System.Text;
using SolarOps.Api.Features.Production;

namespace SolarOps.Api.Tests.Unit.Production;

public sealed class ProductionCsvTests
{
    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    private static readonly ProductionBucket Bucket = new(
        new DateTime(2026, 6, 21, 8, 0, 0, DateTimeKind.Utc), EnergyWh: 1234.5678, PeakPowerW: 5678.9, SampleCount: 12);

    [Fact]
    public void Header_UsesTheSeparator()
    {
        Assert.Equal("period_start_utc;period_start_local;energy_kwh;peak_power_kw;sample_count", ProductionCsv.Header(CsvSeparator.Semicolon));
        Assert.Equal("period_start_utc,period_start_local,energy_kwh,peak_power_kw,sample_count", ProductionCsv.Header(CsvSeparator.Comma));
        Assert.Equal("period_start_utc\tperiod_start_local\tenergy_kwh\tpeak_power_kw\tsample_count", ProductionCsv.Header(CsvSeparator.Tab));
    }

    [Fact]
    public void Semicolon_UsesDecimalCommaForSlovakExcel()
    {
        var row = ProductionCsv.Row(Bucket, CsvSeparator.Semicolon, Bratislava);

        Assert.Equal("2026-06-21T08:00:00Z;2026-06-21 10:00;1,235;5,679;12", row);
    }

    [Theory]
    [InlineData(CsvSeparator.Comma, "2026-06-21T08:00:00Z,2026-06-21 10:00,1.235,5.679,12")]
    [InlineData(CsvSeparator.Tab, "2026-06-21T08:00:00Z\t2026-06-21 10:00\t1.235\t5.679\t12")]
    public void OtherSeparators_UseDecimalPoint(CsvSeparator separator, string expected)
    {
        Assert.Equal(expected, ProductionCsv.Row(Bucket, separator, Bratislava));
    }

    [Fact]
    public async Task WriteAsync_WritesHeaderAndRowsAsUtf8WithCrLf()
    {
        using var stream = new MemoryStream();

        await ProductionCsv.WriteAsync(stream, Rows(), CsvSeparator.Comma, TimeZoneInfo.Utc, CancellationToken.None);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal(
            "period_start_utc,period_start_local,energy_kwh,peak_power_kw,sample_count\r\n" +
            "2026-06-21T08:00:00Z,2026-06-21 08:00,1.235,5.679,12\r\n" +
            "2026-06-21T09:00:00Z,2026-06-21 09:00,0,0,1\r\n",
            text);
        Assert.False(stream.ToArray().AsSpan().StartsWith(Encoding.UTF8.Preamble), "No BOM: the content is ASCII only.");
    }

    private static async IAsyncEnumerable<ProductionBucket> Rows()
    {
        yield return Bucket;
        await Task.Yield();
        yield return new ProductionBucket(new DateTime(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc), 0, 0, 1);
    }
}
