using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SolarOps.Api.Features.Installations;

/// <summary>One device row of the protocol, already formatted for print.</summary>
public sealed record HandoverProtocolDeviceLine(int Position, string Type, string Manufacturer, string Model, string SerialNumber, string Power);

/// <summary>Everything the hand-over protocol shows (#93). Built by the service, so the template has no database access.</summary>
public sealed record HandoverProtocolPdfModel(
    string InstallationName,
    string InstallationId,
    string CustomerName,
    string CommissionedOn,
    string? HandedOverOn,
    string AcceptedBy,
    string? Note,
    string RevisionInspectedOn,
    string RevisionInspector,
    string RevisionResult,
    string? RevisionValidUntil,
    string RevisionFileName,
    string RevisionSha256,
    IReadOnlyList<HandoverProtocolDeviceLine> Devices,
    string GeneratedOn)
{
    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk-SK");

    internal static string Date(DateOnly date) => date.ToString("d. M. yyyy", Sk);

    internal static string Power(int watts) =>
        watts >= 1000 ? (watts / 1000m).ToString("0.###", Sk) + " kW" : watts.ToString(Sk) + " W";

    internal static string DeviceType(DeviceType type) => type switch
    {
        Installations.DeviceType.Panel => "Panel",
        Installations.DeviceType.Inverter => "Invertor",
        _ => type.ToString(),
    };
}

/// <summary>Renders the hand-over protocol. A seam around the PDF library (ADR spike #219).</summary>
public interface IHandoverProtocolPdfGenerator
{
    byte[] Generate(HandoverProtocolPdfModel model);
}

/// <summary>QuestPDF (Community license, ADR spike #219) implementation.</summary>
public sealed class QuestPdfHandoverProtocolGenerator : IHandoverProtocolPdfGenerator
{
    public byte[] Generate(HandoverProtocolPdfModel model) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text("Preberací protokol a dokumentácia FVE").Bold().FontSize(18);
                col.Item().Text(model.InstallationName);
                col.Item().Text($"Zákazník: {model.CustomerName}");
            });

            page.Content().PaddingVertical(15).Column(col =>
            {
                col.Spacing(12);

                col.Item().Text("Uvedenie do prevádzky").Bold().FontSize(12);
                col.Item().Text($"Dátum uvedenia do prevádzky: {model.CommissionedOn}");
                if (model.HandedOverOn is { } handedOver)
                {
                    col.Item().Text($"Dátum odovzdania: {handedOver}");
                }

                col.Item().Text($"Prevzal(a): {model.AcceptedBy}");
                if (!string.IsNullOrWhiteSpace(model.Note))
                {
                    col.Item().Text($"Poznámka: {model.Note}");
                }

                col.Item().Text("Revízna správa").Bold().FontSize(12);
                col.Item().Text($"Dátum revízie: {model.RevisionInspectedOn}");
                col.Item().Text($"Revízny technik: {model.RevisionInspector}");
                col.Item().Text($"Výsledok: {model.RevisionResult}");
                if (model.RevisionValidUntil is { } validUntil)
                {
                    col.Item().Text($"Platnosť do: {validUntil}");
                }

                col.Item().Text($"Súbor: {model.RevisionFileName} (SHA-256 {model.RevisionSha256})").FontSize(8);

                col.Item().Text("Zoznam zariadení").Bold().FontSize(12);
                col.Item().Element(c => Devices(c, model.Devices));
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span($"Vygenerované {model.GeneratedOn}  |  {model.InstallationId}  |  Strana ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        })).GeneratePdf();

    private static void Devices(IContainer container, IReadOnlyList<HandoverProtocolDeviceLine> devices)
    {
        if (devices.Count == 0)
        {
            container.Text("Žiadne evidované zariadenia.");
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(25);
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
                columns.RelativeColumn(3);
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
            });

            table.Header(header =>
            {
                foreach (var title in new[] { "#", "Typ", "Výrobca", "Model", "Sériové číslo", "Výkon" })
                {
                    header.Cell().BorderBottom(1).PaddingBottom(4).Text(title).Bold();
                }
            });

            foreach (var line in devices)
            {
                foreach (var value in new[] { line.Position.ToString(CultureInfo.InvariantCulture), line.Type, line.Manufacturer, line.Model, line.SerialNumber, line.Power })
                {
                    table.Cell().PaddingVertical(2).Text(value);
                }
            }
        });
    }
}
