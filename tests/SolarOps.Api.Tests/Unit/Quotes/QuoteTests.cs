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

    private static Quote Draft() => Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now);

    [Fact]
    public void Create_RecordsInitialHistoryEntry()
    {
        var change = Assert.Single(Draft().StateChanges);

        Assert.Null(change.FromStatus);
        Assert.Equal(QuoteStatus.Draft, change.ToStatus);
        Assert.Equal(UserId, change.ChangedBy);
    }

    [Fact]
    public void Send_SetsValidityAndStatus_AndAppendsHistory()
    {
        var quote = Draft();

        quote.Send(14, UserId, "Odoslané e-mailom", Now.AddHours(1));

        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.Equal(Now.AddHours(1).AddDays(14), quote.ValidUntil);
        Assert.Equal(2, quote.StateChanges.Count);
        Assert.Equal("Odoslané e-mailom", quote.StateChanges[1].Note);
    }

    [Theory]
    [InlineData(QuoteStatus.Accepted)]
    [InlineData(QuoteStatus.Rejected)]
    [InlineData(QuoteStatus.Expired)]
    public void FinalStates_AllowNoTransition(QuoteStatus final)
    {
        var quote = Draft();
        quote.Send(30, UserId, null, Now);
        switch (final)
        {
            case QuoteStatus.Accepted: quote.Accept(UserId, null, Now); break;
            case QuoteStatus.Rejected: quote.Reject(UserId, null, Now); break;
            default: quote.Expire(Now.AddDays(31)); break;
        }

        Assert.Empty(quote.AllowedTransitions);
        Assert.Throws<DomainException>(() => quote.Send(30, UserId, null, Now));
        Assert.Throws<DomainException>(() => quote.Accept(UserId, null, Now));
        Assert.Throws<DomainException>(() => quote.Reject(UserId, null, Now));
        Assert.Throws<DomainException>(() => quote.Expire(Now));
    }

    [Fact]
    public void Draft_CannotBeAcceptedRejectedOrExpired()
    {
        var quote = Draft();

        Assert.Throws<DomainException>(() => quote.Accept(UserId, null, Now));
        Assert.Throws<DomainException>(() => quote.Reject(UserId, null, Now));
        Assert.Throws<DomainException>(() => quote.Expire(Now));
        Assert.Equal(QuoteStatus.Draft, quote.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Send_InvalidValidity_Throws_AndKeepsDraft(int days)
    {
        var quote = Draft();

        Assert.Throws<RequestValidationException>(() => quote.Send(days, UserId, null, Now));
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Null(quote.ValidUntil);
    }

    [Fact]
    public void IsOverdue_TrueFromValidUntilOnwards_OnlyForSentQuotes()
    {
        var quote = Draft();
        Assert.False(quote.IsOverdue(Now.AddYears(1)));

        quote.Send(10, UserId, null, Now);

        Assert.False(quote.IsOverdue(Now.AddDays(10).AddTicks(-1)));
        Assert.True(quote.IsOverdue(Now.AddDays(10)));
    }

    [Fact]
    public void Expire_IsASystemChangeWithoutUser()
    {
        var quote = Draft();
        quote.Send(1, UserId, null, Now);

        quote.Expire(Now.AddDays(1));

        var last = quote.StateChanges[^1];
        Assert.Equal(QuoteStatus.Expired, quote.Status);
        Assert.Null(last.ChangedBy);
    }

    [Fact]
    public void Create_FromDesign_RecordsDesignLink()
    {
        var designId = Guid.CreateVersion7();

        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now, designId, 3);

        Assert.Equal(designId, quote.DesignId);
        Assert.Equal(3, quote.DesignVersionNumber);
        Assert.False(quote.NeedsRecalculation);
    }

    [Fact]
    public void Create_WithoutDesign_HasNoLink()
    {
        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now);

        Assert.Null(quote.DesignId);
        Assert.Null(quote.DesignVersionNumber);
    }

    [Fact]
    public void MarkForRecalculation_DraftLinkedToDesign_SetsFlag()
    {
        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now, Guid.CreateVersion7(), 1);

        var changed = quote.MarkForRecalculation(Now.AddDays(1));

        Assert.True(changed);
        Assert.True(quote.NeedsRecalculation);
        Assert.Equal(Now.AddDays(1), quote.RecalculationRequestedAt);
        Assert.Equal(Now.AddDays(1), quote.UpdatedAt);
    }

    [Fact]
    public void MarkForRecalculation_SentQuote_SetsFlag()
    {
        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now, Guid.CreateVersion7(), 1);
        quote.Send(30, UserId, null, Now);

        Assert.True(quote.MarkForRecalculation(Now.AddDays(1)));
        Assert.Equal(QuoteStatus.Sent, quote.Status);
    }

    [Fact]
    public void MarkForRecalculation_AlreadyMarked_ReturnsFalse()
    {
        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now, Guid.CreateVersion7(), 1);
        quote.MarkForRecalculation(Now);

        Assert.False(quote.MarkForRecalculation(Now.AddDays(1)));
        Assert.Equal(Now, quote.RecalculationRequestedAt);
    }

    [Fact]
    public void MarkForRecalculation_AcceptedQuote_IsIgnored()
    {
        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now, Guid.CreateVersion7(), 1);
        quote.Send(30, UserId, null, Now);
        quote.Accept(UserId, null, Now);

        Assert.False(quote.MarkForRecalculation(Now.AddDays(1)));
        Assert.False(quote.NeedsRecalculation);
    }

    [Fact]
    public void MarkForRecalculation_QuoteWithoutDesign_IsIgnored()
    {
        var quote = Quote.Create(CustomerId, [Panel()], 23m, null, UserId, Now);

        Assert.False(quote.MarkForRecalculation(Now));
    }
}
