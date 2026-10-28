using System.Text;
using System.Text.RegularExpressions;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Contracts;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;

namespace SolarOps.Api.Tests.Unit.Contracts;

public sealed class ContractPdfTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    static ContractPdfTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static Customer Company() => Customer.Create(
        new CustomerDetails(
            CustomerType.Company, null, null, "Slnko Ľščťžýáíé s.r.o.", "35763469", "2020273893", "SK2020273893", "info@slnko.sk", "+421 900 123 456",
            new PostalAddress("Hlavná 12", "Žilina", "010 01"), null).Normalize(),
        null,
        Now);

    private static Contract ContractWith(int lineCount)
    {
        var lines = Enumerable.Range(1, lineCount)
            .Select(i => new ContractLineData(Guid.CreateVersion7(), ComponentType.Panel, $"PAN-{i}", $"Panel {i} ňôäĽŠČŤŽ", 12345.67m, 2m, 24691.34m))
            .ToList();
        return Contract.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 23m, lines, new QuoteTotals(1000m, 230m, 1230m), Guid.CreateVersion7(), Now);
    }

    [Fact]
    public void Model_ContainsPartiesLinesAndTotals_InSlovakFormat()
    {
        var contract = ContractWith(2);
        var customer = Company();

        var model = ContractPdfModel.From(contract, customer);

        Assert.Equal(contract.Number, model.Number);
        Assert.Equal("Slnko Ľščťžýáíé s.r.o.", model.Buyer.Name);
        Assert.Contains("IČO: 35763469", model.Buyer.Details);
        Assert.Contains("010 01 Žilina", model.Buyer.Details);
        Assert.Equal(ContractPdfModel.SellerName, model.Seller.Name);
        Assert.Equal(2, model.Lines.Count);
        Assert.Equal(["PAN-1", "PAN-2"], model.Lines.Select(l => l.Code));
        // sk-SK uses a non-breaking space as the thousands separator and a decimal comma.
        Assert.Equal("12 345,67 €", model.Lines[0].UnitPrice);
        Assert.Equal("1 230,00 €", model.GrossTotal);
        Assert.Equal("23 %", model.VatRateText);
        Assert.Equal("25. 10. 2026", model.CreatedOn);
        Assert.Null(model.SignedOn);
        Assert.Equal("Návrh", model.StatusText);
    }

    [Fact]
    public void Generator_ProducesPdfDocument()
    {
        var model = ContractPdfModel.From(ContractWith(3), Company());

        var pdf = new QuestPdfContractGenerator().Generate(model);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void Generator_LongTable_SpansMultiplePages()
    {
        var generator = new QuestPdfContractGenerator();
        var one = generator.Generate(ContractPdfModel.From(ContractWith(2), Company()));
        var many = generator.Generate(ContractPdfModel.From(ContractWith(120), Company()));

        Assert.Equal(1, PageCount(one));
        Assert.True(PageCount(many) > 1);
    }

    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b(?!s)").Count;
}
