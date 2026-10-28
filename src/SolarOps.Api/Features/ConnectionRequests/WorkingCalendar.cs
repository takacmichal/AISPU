namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Working days of the Slovak calendar (Monday to Friday except public holidays), used for the deadlines in ADR #203/#217.</summary>
public static class WorkingCalendar
{
    /// <summary>The date <paramref name="count"/> working days after <paramref name="start"/>; the start day itself is not counted.</summary>
    public static DateOnly AddWorkingDays(DateOnly start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var date = start;
        for (var remaining = count; remaining > 0;)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date))
            {
                remaining--;
            }
        }

        return date;
    }

    public static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsPublicHoliday(date);

    /// <summary>Public holidays of the Slovak Republic (zákon 241/1993): fixed dates plus Good Friday and Easter Monday.</summary>
    public static bool IsPublicHoliday(DateOnly date)
    {
        var fixedHoliday = (date.Month, date.Day) is
            (1, 1) or (1, 6) or (5, 1) or (5, 8) or (7, 5) or (8, 29) or (9, 1) or (9, 15)
            or (11, 1) or (11, 17) or (12, 24) or (12, 25) or (12, 26);
        if (fixedHoliday)
        {
            return true;
        }

        var easterSunday = EasterSunday(date.Year);
        return date == easterSunday.AddDays(-2) || date == easterSunday.AddDays(1);
    }

    // Anonymous Gregorian algorithm (Meeus/Jones/Butcher).
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
