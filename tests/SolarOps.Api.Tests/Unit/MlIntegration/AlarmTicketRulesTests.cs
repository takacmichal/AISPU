using SolarOps.Api.Features.MlIntegration;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Tests.Unit.MlIntegration;

public sealed class AlarmTicketRulesTests
{
    private static readonly MlOptions Options = new() { TicketMediumLostKwh = 10, TicketHighLostKwh = 50 };

    [Theory]
    [InlineData(ServiceTicketPriority.Low, 9.99, ServiceTicketPriority.Low)]
    [InlineData(ServiceTicketPriority.Low, 10, ServiceTicketPriority.Medium)]
    [InlineData(ServiceTicketPriority.Low, 50, ServiceTicketPriority.High)]
    [InlineData(ServiceTicketPriority.Medium, 1, ServiceTicketPriority.Medium)]
    [InlineData(ServiceTicketPriority.High, 0, ServiceTicketPriority.High)]
    public void Priority_LossCanOnlyRaiseWhatTheDetectionDecided(ServiceTicketPriority detected, double lostKwh, ServiceTicketPriority expected)
    {
        var alarm = Alarm() with { TicketPriority = detected, EstimatedLostKwh = lostKwh };

        Assert.Equal(expected, AlarmTicketRules.Priority(alarm, Options));
    }

    [Fact]
    public void Title_NamesTheInstallation_AndFitsTheColumn()
    {
        Assert.Equal("Podvýroba: FVE Nitra", AlarmTicketRules.Title("FVE Nitra"));
        Assert.Equal(ServiceTicket.TitleMaxLength, AlarmTicketRules.Title(new string('x', 500)).Length);
    }

    [Fact]
    public void Description_HasTheDeviationAndEvidenceInLocalTime()
    {
        var alarm = Alarm() with
        {
            Evidence = [new MlEvidenceHour(new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero), 1500, 3000, 0.5)],
        };

        var text = AlarmTicketRules.Description("FVE Nitra", alarm);

        Assert.Contains("FVE Nitra", text, StringComparison.Ordinal);
        Assert.Contains("Podvýroba 5 h", text, StringComparison.Ordinal);
        Assert.Contains("60 %", text, StringComparison.Ordinal);
        Assert.Contains("12,5 kWh", text, StringComparison.Ordinal);
        // 10:00 UTC is 12:00 in Bratislava in June; the evidence is labelled by the end of the hour.
        Assert.Contains("Dôkazy po hodinách (čas je koniec hodiny):", text, StringComparison.Ordinal);
        Assert.Contains("21. 6. 2026 12:00: 1,5 kW z očakávaných 3 kW (50 %)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_FitsTheColumn_WithALongEvent()
    {
        var hour = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        var alarm = Alarm() with
        {
            Reason = new string('x', 1000),
            Evidence = Enumerable.Range(0, 2000).Select(i => new MlEvidenceHour(hour.AddHours(i), 1500, 3000, 0.5)).ToList(),
        };

        Assert.True(AlarmTicketRules.Description("FVE Nitra", alarm).Length <= ServiceTicket.DescriptionMaxLength);
    }

    private static MlAlarm Alarm() => new(
        Guid.CreateVersion7(),
        new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
        null,
        new DateTimeOffset(2026, 6, 21, 11, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 21, 14, 0, 0, TimeSpan.Zero),
        true,
        UnderproductionSeverity.Medium,
        ServiceTicketPriority.Medium,
        0.6,
        0.5,
        5,
        5,
        5,
        31.25,
        18.75,
        12.5,
        "sustained_underproduction",
        "Podvýroba 5 h",
        []);
}
