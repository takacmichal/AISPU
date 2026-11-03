using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Party of the contract as printed in the PDF: a name and address/identification lines.</summary>
public sealed record ContractPdfParty(string Name, IReadOnlyList<string> Details);

/// <summary>One row of the item table, already formatted for print.</summary>
public sealed record ContractPdfLine(int Position, string Code, string Name, string Quantity, string UnitPrice, string NetPrice);

/// <summary>Everything the PDF shows. Built from the contract and its customer, so the template has no database access.</summary>
public sealed record ContractPdfModel(
    string Number,
    string StatusText,
    string CreatedOn,
    string? SignedOn,
    ContractPdfParty Seller,
    ContractPdfParty Buyer,
    IReadOnlyList<ContractPdfLine> Lines,
    string VatRateText,
    string NetTotal,
    string VatTotal,
    string GrossTotal)
{
    /// <summary>Seller name; company details (IČO, address) are not configured anywhere yet.</summary>
    internal const string SellerName = "SolarOps";

    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk-SK");

    public static ContractPdfModel From(Contract contract, Customer customer) => new(
        contract.Number,
        contract.Status == ContractStatus.Signed ? "Podpísaná" : "Návrh",
        Date(DateOnly.FromDateTime(contract.CreatedAt.UtcDateTime)),
        contract.SignedOn is { } signed ? Date(signed) : null,
        new ContractPdfParty(SellerName, []),
        new ContractPdfParty(customer.DisplayName, BuyerDetails(customer)),
        contract.Lines.OrderBy(l => l.Position)
            .Select(l => new ContractPdfLine(l.Position, l.Code, l.Name, l.Quantity.ToString("0.##", Sk), Money(l.UnitPrice), Money(l.NetPrice)))
            .ToList(),
        contract.VatRatePercent.ToString("0.##", Sk) + " %",
        Money(contract.NetTotal),
        Money(contract.VatTotal),
        Money(contract.GrossTotal));

    /// <summary>Amount in the Slovak format, e.g. <c>12 345,67 €</c>.</summary>
    internal static string Money(decimal amount) => amount.ToString("N2", Sk) + " €";

    private static string Date(DateOnly date) => date.ToString("d. M. yyyy", Sk);

    private static List<string> BuyerDetails(Customer customer)
    {
        var details = new List<string>
        {
            customer.Address.Street,
            $"{customer.Address.PostalCode} {customer.Address.City}".Trim(),
        };
        AddIfPresent(details, "IČO", customer.Ico);
        AddIfPresent(details, "DIČ", customer.Dic);
        AddIfPresent(details, "IČ DPH", customer.IcDph);
        AddIfPresent(details, "E-mail", customer.Email);
        AddIfPresent(details, "Telefón", customer.Phone);
        return details.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
    }

    private static void AddIfPresent(List<string> details, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details.Add($"{label}: {value}");
        }
    }
}

/// <summary>Renders contract PDFs. A seam around the PDF library (ADR spike #219), so the library can be replaced locally.</summary>
public interface IContractPdfGenerator
{
    byte[] Generate(ContractPdfModel model);
}

/// <summary>QuestPDF (Community license, see ADR spike #219) implementation; the template is plain C#.</summary>
public sealed class QuestPdfContractGenerator : IContractPdfGenerator
{
    public byte[] Generate(ContractPdfModel model) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text($"Zmluva o dielo č. {model.Number}").Bold().FontSize(18);
                col.Item().Text($"Stav: {model.StatusText}");
                col.Item().Text($"Dátum vytvorenia: {model.CreatedOn}");
                if (model.SignedOn is { } signedOn)
                {
                    col.Item().Text($"Dátum podpisu: {signedOn}");
                }
            });

            page.Content().PaddingVertical(15).Column(col =>
            {
                col.Spacing(15);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Party(c, "Predávajúci", model.Seller));
                    row.RelativeItem().Element(c => Party(c, "Kupujúci", model.Buyer));
                });
                col.Item().Element(c => Items(c, model));
                col.Item().AlignRight().Column(totals =>
                {
                    totals.Item().Text($"Základ dane: {model.NetTotal}");
                    totals.Item().Text($"DPH {model.VatRateText}: {model.VatTotal}");
                    totals.Item().Text($"Celkom s DPH: {model.GrossTotal}").Bold().FontSize(12);
                });
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span($"{model.Number}  |  Strana ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        })).GeneratePdf();

    private static void Party(IContainer container, string title, ContractPdfParty party) =>
        container.Column(col =>
        {
            col.Item().Text(title).Bold();
            col.Item().Text(party.Name);
            foreach (var detail in party.Details)
            {
                col.Item().Text(detail);
            }
        });

    private static void Items(IContainer container, ContractPdfModel model) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(25);
                c.ConstantColumn(80);
                c.RelativeColumn();
                c.ConstantColumn(45);
                c.ConstantColumn(70);
                c.ConstantColumn(75);
            });

            table.Header(h =>
            {
                h.Cell().Element(HeaderCell).Text("#");
                h.Cell().Element(HeaderCell).Text("Kód");
                h.Cell().Element(HeaderCell).Text("Položka");
                h.Cell().Element(HeaderCell).AlignRight().Text("Množ.");
                h.Cell().Element(HeaderCell).AlignRight().Text("Jedn. cena");
                h.Cell().Element(HeaderCell).AlignRight().Text("Cena bez DPH");
            });

            foreach (var line in model.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.Position.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(line.Code);
                table.Cell().Element(BodyCell).Text(line.Name);
                table.Cell().Element(BodyCell).AlignRight().Text(line.Quantity);
                table.Cell().Element(BodyCell).AlignRight().Text(line.UnitPrice);
                table.Cell().Element(BodyCell).AlignRight().Text(line.NetPrice);
            }
        });

    private static IContainer HeaderCell(IContainer c) =>
        c.DefaultTextStyle(t => t.Bold()).BorderBottom(1).PaddingVertical(3).PaddingHorizontal(2);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(2);
}
