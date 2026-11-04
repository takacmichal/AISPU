using SolarOps.Api.Features.Production;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Production;

public sealed class ProductionQueryTests
{
    private static Dictionary<string, string[]> Errors(object query) => DataAnnotationsValidator.Validate(query);

    [Fact]
    public void UtcTimestamps_AreParsed_AndHourIsTheDefaultGranularity()
    {
        var query = new ProductionQuery { From = "2026-06-21T00:00:00Z", To = "2026-06-22T00:00:00Z" };

        Assert.Empty(Errors(query));
        var period = query.ToPeriod();
        Assert.Equal(new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), period.To);
        Assert.Equal(ProductionGranularity.Hour, period.Granularity);
        Assert.Equal(ProductionQuery.DefaultTimeZone, period.TimeZoneId);
        Assert.Equal(TimeSpan.Zero, period.From.Offset);
    }

    [Fact]
    public void ExplicitOffset_IsConvertedToUtc()
    {
        var query = new ProductionQuery { From = "2026-06-21T02:00:00+02:00", To = "2026-06-21T12:00:00+02:00" };

        var period = query.ToPeriod();

        Assert.Equal(new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(TimeSpan.Zero, period.From.Offset);
    }

    [Fact]
    public void DateOnly_IsLocalMidnightInTheRequestedTimeZone()
    {
        var query = new ProductionQuery { From = "2026-06-01", To = "2026-07-01", Granularity = "day" };

        var period = query.ToPeriod();

        // Europe/Bratislava is UTC+2 in summer.
        Assert.Equal(new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero), period.To);
        Assert.Equal(ProductionGranularity.Day, period.Granularity);
    }

    [Fact]
    public void DateOnly_UsesUtcWhenRequested()
    {
        var query = new ProductionQuery { From = "2026-01-01", To = "2026-02-01", Granularity = "Month", TimeZone = "UTC" };

        var period = query.ToPeriod();

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal("UTC", period.TimeZoneId);
    }

    [Theory]
    [InlineData(null, "2026-06-22T00:00:00Z", "from")]
    [InlineData("2026-06-21T00:00:00Z", null, "to")]
    [InlineData("yesterday", "2026-06-22T00:00:00Z", "from")]
    [InlineData("2026-06-21T00:00:00", "2026-06-22T00:00:00Z", "from")]
    [InlineData("2026-06-22T00:00:00Z", "2026-06-22T00:00:00Z", "to")]
    [InlineData("2026-06-23T00:00:00Z", "2026-06-22T00:00:00Z", "to")]
    public void InvalidPeriod_IsRejected(string? from, string? to, string key)
    {
        var errors = Errors(new ProductionQuery { From = from, To = to });

        Assert.Contains(key, errors.Keys);
    }

    [Fact]
    public void UnknownGranularity_IsRejected()
    {
        var errors = Errors(new ProductionQuery { From = "2026-06-21", To = "2026-06-22", Granularity = "week" });

        Assert.Contains("granularity", errors.Keys);
    }

    [Fact]
    public void UnknownTimeZone_IsRejected()
    {
        var errors = Errors(new ProductionQuery { From = "2026-06-21", To = "2026-06-22", TimeZone = "Mars/Olympus" });

        Assert.Contains("timeZone", errors.Keys);
    }

    [Theory]
    [InlineData("fiveMinutes", "2026-06-01T00:00:00Z", "2026-07-02T00:00:01Z")]
    [InlineData("hour", "2025-01-01T00:00:00Z", "2026-01-02T00:00:01Z")]
    [InlineData("day", "2016-01-01T00:00:00Z", "2026-01-31T00:00:00Z")]
    public void TooLongRange_IsRejected(string granularity, string from, string to)
    {
        var errors = Errors(new ProductionQuery { From = from, To = to, Granularity = granularity });

        Assert.Contains("to", errors.Keys);
    }

    [Theory]
    [InlineData("fiveMinutes", "2026-06-01T00:00:00Z", "2026-07-02T00:00:00Z")]
    [InlineData("hour", "2025-01-01T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("month", "2016-06-01T00:00:00Z", "2026-06-01T00:00:00Z")]
    public void MaximalRange_IsAccepted(string granularity, string from, string to)
    {
        // 31 days of 5-minute buckets, 366 days of hours, 3660 days of days or months.
        var errors = Errors(new ProductionQuery { From = from, To = to, Granularity = granularity });

        Assert.Empty(errors);
    }

    [Fact]
    public void Export_AllowsAYearOfFiveMinuteBuckets()
    {
        var query = new ProductionExportQuery { From = "2025-06-01T00:00:00Z", To = "2026-06-01T00:00:00Z", Granularity = "fiveMinutes" };

        Assert.Empty(Errors(query));
        Assert.Equal(CsvSeparator.Semicolon, query.ToSeparator());
    }

    [Theory]
    [InlineData("comma", CsvSeparator.Comma)]
    [InlineData("SEMICOLON", CsvSeparator.Semicolon)]
    [InlineData("tab", CsvSeparator.Tab)]
    public void Export_ParsesTheSeparator(string value, CsvSeparator expected)
    {
        var query = new ProductionExportQuery { From = "2026-06-21", To = "2026-06-22", Separator = value };

        Assert.Empty(Errors(query));
        Assert.Equal(expected, query.ToSeparator());
    }

    [Theory]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("pipe")]
    public void Export_RejectsAnUnknownSeparator(string value)
    {
        var errors = Errors(new ProductionExportQuery { From = "2026-06-21", To = "2026-06-22", Separator = value });

        Assert.Contains("separator", errors.Keys);
    }

    [Fact]
    public void Comparison_RejectsFiveMinuteGranularity()
    {
        var errors = Errors(new ProductionComparisonQuery { From = "2026-06-21", To = "2026-06-22", Granularity = "fiveMinutes" });

        Assert.Contains("granularity", errors.Keys);
    }

    [Fact]
    public void ToPeriod_ThrowsForAnInvalidQuery()
    {
        Assert.Throws<SolarOps.Api.Infrastructure.Errors.RequestValidationException>(() => new ProductionQuery().ToPeriod());
    }
}
