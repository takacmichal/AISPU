using System.Globalization;
using System.Text;
using SolarOps.Api.Features.Production;
using SolarOps.Api.Features.Tickets;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>What a service ticket created from an alarm says (#115) and how urgent it is (#117).</summary>
internal static class AlarmTicketRules
{
    private static readonly CultureInfo Slovak = CultureInfo.GetCultureInfo("sk-SK");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ProductionQuery.DefaultTimeZone);

    /// <summary>
    /// The detection decides the priority from the ratio and duration (ADR #194). A large loss in kWh can only raise it:
    /// a big plant loses a lot even at a mild ratio, a small one must not be lowered below what the ADR says.
    /// </summary>
    public static ServiceTicketPriority Priority(MlAlarm alarm, MlOptions options)
    {
        var byLoss = alarm.EstimatedLostKwh >= options.TicketHighLostKwh ? ServiceTicketPriority.High
            : alarm.EstimatedLostKwh >= options.TicketMediumLostKwh ? ServiceTicketPriority.Medium
            : ServiceTicketPriority.Low;
        return byLoss > alarm.TicketPriority ? byLoss : alarm.TicketPriority;
    }

    public static string Title(string installationName)
    {
        var title = $"Podvýroba: {installationName}";
        return title.Length <= ServiceTicket.TitleMaxLength ? title : title[..ServiceTicket.TitleMaxLength];
    }

    /// <summary>The deviation and the hours that prove it; the column limit cuts the evidence, never the summary.</summary>
    public static string Description(string installationName, MlAlarm alarm)
    {
        var text = new StringBuilder()
            .AppendLine(alarm.Reason)
            .AppendLine()
            .AppendLine(string.Create(Slovak, $"Elektráreň: {installationName}"))
            .AppendLine(string.Create(Slovak, $"Obdobie: {Local(alarm.Start.AddHours(-1))} – {Local(alarm.LastUnderproductionAt)}"))
            .AppendLine(string.Create(Slovak, $"Výroba oproti predikcii: {Math.Round(alarm.MedianRatio * 100)} % (medián), najmenej {Math.Round(alarm.MinRatio * 100)} %"))
            .AppendLine(string.Create(Slovak, $"Očakávaná výroba {alarm.ExpectedKwh:0.#} kWh, skutočná {alarm.ActualKwh:0.#} kWh, odhadovaná strata {alarm.EstimatedLostKwh:0.#} kWh"));

        if (alarm.Evidence.Count > 0)
        {
            text.AppendLine().AppendLine("Dôkazy po hodinách (čas je koniec hodiny):");
            foreach (var hour in alarm.Evidence)
            {
                var line = string.Create(
                    Slovak,
                    $"{Local(hour.Timestamp)}: {hour.ActualPowerW / 1000:0.##} kW z očakávaných {hour.ExpectedPowerW / 1000:0.##} kW ({Math.Round(hour.Ratio * 100)} %)");
                if (text.Length + line.Length + Environment.NewLine.Length > ServiceTicket.DescriptionMaxLength)
                {
                    break;
                }

                text.AppendLine(line);
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string Local(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone).ToString("d. M. yyyy HH:mm", Slovak);
}
