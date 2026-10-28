using SolarOps.Api.Features.ConnectionRequests;

namespace SolarOps.Api.Tests.Unit.ConnectionRequests;

public sealed class WorkingCalendarTests
{
    [Fact]
    public void AddWorkingDays_SkipsWeekends()
    {
        // Friday + 1 working day = Monday.
        Assert.Equal(new DateOnly(2026, 10, 26), WorkingCalendar.AddWorkingDays(new DateOnly(2026, 10, 23), 1));
        // Tuesday + 10 working days = Tuesday two weeks later.
        Assert.Equal(new DateOnly(2026, 11, 10), WorkingCalendar.AddWorkingDays(new DateOnly(2026, 10, 27), 10));
    }

    [Fact]
    public void AddWorkingDays_StartingOnWeekend_CountsFromNextWorkingDay()
    {
        // Saturday + 1 working day = Monday.
        Assert.Equal(new DateOnly(2026, 10, 26), WorkingCalendar.AddWorkingDays(new DateOnly(2026, 10, 24), 1));
    }

    [Fact]
    public void AddWorkingDays_SkipsEasterHolidays()
    {
        // Easter 2026: Good Friday 3 April, Easter Monday 6 April.
        Assert.True(WorkingCalendar.IsPublicHoliday(new DateOnly(2026, 4, 3)));
        Assert.True(WorkingCalendar.IsPublicHoliday(new DateOnly(2026, 4, 6)));
        Assert.Equal(new DateOnly(2026, 4, 7), WorkingCalendar.AddWorkingDays(new DateOnly(2026, 4, 2), 1));
    }

    [Fact]
    public void AddWorkingDays_SkipsChristmas()
    {
        // 24, 25 and 26 December are holidays; 27 is a Sunday.
        Assert.Equal(new DateOnly(2026, 12, 29), WorkingCalendar.AddWorkingDays(new DateOnly(2026, 12, 23), 2));
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 1, 6)]
    [InlineData(2026, 5, 1)]
    [InlineData(2026, 5, 8)]
    [InlineData(2026, 7, 5)]
    [InlineData(2026, 8, 29)]
    [InlineData(2026, 9, 1)]
    [InlineData(2026, 9, 15)]
    [InlineData(2026, 11, 1)]
    [InlineData(2026, 11, 17)]
    public void IsPublicHoliday_RecognisesFixedSlovakHolidays(int year, int month, int day) =>
        Assert.True(WorkingCalendar.IsPublicHoliday(new DateOnly(year, month, day)));

    [Fact]
    public void IsPublicHoliday_OrdinaryDayIsNot() =>
        Assert.False(WorkingCalendar.IsPublicHoliday(new DateOnly(2026, 10, 27)));

    [Fact]
    public void AddWorkingDays_WithNonPositiveCount_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkingCalendar.AddWorkingDays(new DateOnly(2026, 10, 27), 0));
}
