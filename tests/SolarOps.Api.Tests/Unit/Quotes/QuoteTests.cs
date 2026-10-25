using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Quotes;

public sealed class QuoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static QuoteLineData Panel(decimal price = 120.50m, decimal quantity = 10m) =>
        new(Guid.CreateVersion7(), ComponentType.Panel, "PAN-450", "Panel 450 W", price, quantity);

    private static QuoteLineData Labor(decimal price = 35m, decimal quantity = 8.5m) =>
        new(Guid.CreateVersion7(), ComponentType.Labor, "LAB", "Montáž", price, quantity);

    [Theory]
    [InlineData(10.00, 3, 30.00)]
    [InlineData(0.333, 3, 1.00)]
    [InlineData(0.005, 1, 0.01)]
    [InlineData(35, 8.5, 297.50)]
    public void LineNet_RoundsToCents_AwayFromZero(double unitPrice, double quantity, double expected) =>
        Assert.Equal((decimal)expected, QuotePricing.LineNet((decimal)unitPrice, (decimal)quantity));

    [Fact]
    public void Calculate_AddsVatOnceFromNetTotal()
    {
        var totals = QuotePricing.Calculate([1205.00m, 950.00m, 297.50m], 23m);

        Assert.Equal(2452.50m, totals.Net);
        Assert.Equal(564.08m, totals.Vat); // 564.075 rounds up
        Assert.Equal(3016.58m, totals.Gross);
    }

    [Fact]
    public void Calculate_ZeroVat_GrossEqualsNet()
    {
        var totals = QuotePricing.Calculate([100m, 50.25m], 0m);

        Assert.Equal(new QuoteTotals(150.25m, 0m, 150.25m), totals);
    }

    [Fact]
    public void Create_StartsAsDraft_CopiesLinesAndComputesTotals()
    {
        var quote = Quote.Create(CustomerId, [Panel(), Labor()], 23m, "  Poznámka ", UserId, Now);

        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal("Poznámka", quote.Note);
        Assert.Equal([1, 2], quote.Lines.Select(l => l.Position));
        Assert.Equal([1205.00m, 297.50m], quote.Lines.Select(l => l.NetPrice));
        Assert.Equal(1502.50m, quote.NetTotal);
        Assert.Equal(345.58m, quote.VatTotal); // 345.575 rounds up
        Assert.Equal(1848.08m, quote.GrossTotal);
        Assert.Equal(UserId, quote.CreatedBy);
        Assert.Equal(Now, quote.CreatedAt);
    }

    [Fact]
    public void Create_NoLines_Throws() =>
        Assert.Throws<RequestValidationException>(() => Quote.Create(CustomerId, [], 23m, null, UserId, Now));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    [InlineData(2.5)]
    public void Create_InvalidPanelQuantity_ThrowsOnThatLine(double quantity)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            Quote.Create(CustomerId, [Panel(), Panel(quantity: (decimal)quantity)], 23m, null, UserId, Now));

        Assert.Contains("Lines[1].Quantity", ex.Errors.Keys);
        Assert.DoesNotContain("Lines[0].Quantity", ex.Errors.Keys);
    }

    [Fact]
    public void Create_LaborMayHaveFractionalHours_ButNotMoreThanTwoDecimals()
    {
        Quote.Create(CustomerId, [Labor(quantity: 2.25m)], 23m, null, UserId, Now);

        Assert.Throws<RequestValidationException>(() =>
            Quote.Create(CustomerId, [Labor(quantity: 2.255m)], 23m, null, UserId, Now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    [InlineData(19.999)]
    public void Create_InvalidVatRate_Throws(double vat)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            Quote.Create(CustomerId, [Panel()], (decimal)vat, null, UserId, Now));

        Assert.Contains("VatRatePercent", ex.Errors.Keys);
    }
}
