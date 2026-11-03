using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Quotes;

public sealed class QuoteValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static QuoteLineData Line(ComponentType type = ComponentType.Panel, decimal price = 100m, decimal quantity = 1m) =>
        new(Guid.CreateVersion7(), type, "CODE", "Name", price, quantity);

    private static Dictionary<string, string[]> CreateErrors(IReadOnlyList<QuoteLineData> lines, decimal vat = 23m, string? note = null) =>
        Assert.Throws<RequestValidationException>(() => Quote.Create(CustomerId, lines, vat, note, UserId, Now)).Errors
            .ToDictionary(e => e.Key, e => e.Value);

    private static Quote Draft() => Quote.Create(CustomerId, [Line()], 23m, null, UserId, Now);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(23.005)]
    public void Create_InvalidVatRate_Throws(double vat) =>
        Assert.Contains("VatRatePercent", CreateErrors([Line()], (decimal)vat).Keys);

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(19.5)]
    public void Create_VatRateOnBoundary_IsAccepted(double vat) =>
        Assert.Equal((decimal)vat, Quote.Create(CustomerId, [Line()], (decimal)vat, null, UserId, Now).VatRatePercent);

    [Fact]
    public void Create_TooLongNote_Throws() =>
        Assert.Contains("Note", CreateErrors([Line()], note: new string('n', Quote.NoteMaxLength + 1)).Keys);

    [Fact]
    public void Create_BlankNote_IsStoredAsNull() =>
        Assert.Null(Quote.Create(CustomerId, [Line()], 23m, "   ", UserId, Now).Note);

    [Fact]
    public void Create_MaxLines_IsAccepted_AndOneOverIsRejected()
    {
        var lines = Enumerable.Range(0, Quote.MaxLines).Select(_ => Line()).ToList();
        Assert.Equal(Quote.MaxLines, Quote.Create(CustomerId, lines, 23m, null, UserId, Now).Lines.Count);

        lines.Add(Line());
        Assert.Contains("Lines", CreateErrors(lines).Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Create_QuantityOutOfRange_Throws(double quantity) =>
        Assert.Contains("Lines[0].Quantity", CreateErrors([Line(quantity: (decimal)quantity)]).Keys);

    [Fact]
    public void Create_MaxQuantity_IsAccepted() =>
        Assert.Single(Quote.Create(CustomerId, [Line(quantity: Quote.MaxQuantity)], 23m, null, UserId, Now).Lines);

    [Theory]
    [InlineData(ComponentType.Panel)]
    [InlineData(ComponentType.Inverter)]
    public void Create_FractionalPiecesOfHardware_Throws(ComponentType type) =>
        Assert.Contains("Lines[0].Quantity", CreateErrors([Line(type, quantity: 1.5m)]).Keys);

    [Fact]
    public void Create_LaborWithThreeDecimals_Throws() =>
        Assert.Contains("Lines[0].Quantity", CreateErrors([Line(ComponentType.Labor, quantity: 1.005m)]).Keys);

    [Fact]
    public void Create_ReportsErrorOfEveryInvalidLine()
    {
        var errors = CreateErrors([Line(quantity: 0), Line(), Line(quantity: 0)]);

        Assert.Contains("Lines[0].Quantity", errors.Keys);
        Assert.DoesNotContain("Lines[1].Quantity", errors.Keys);
        Assert.Contains("Lines[2].Quantity", errors.Keys);
    }

    [Fact]
    public void Create_TotalsOfRoundedLines_AreNotRecomputedFromUnrounded()
    {
        // 3 x 0.333 = 0.999 -> 1.00 per line; two lines -> 2.00 net, VAT 0.46.
        var quote = Quote.Create(CustomerId, [Line(ComponentType.Labor, 0.333m, 3m), Line(ComponentType.Labor, 0.333m, 3m)], 23m, null, UserId, Now);

        Assert.Equal(2.00m, quote.NetTotal);
        Assert.Equal(0.46m, quote.VatTotal);
        Assert.Equal(2.46m, quote.GrossTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(366)]
    public void Send_ValidityOutOfRange_ThrowsAndKeepsDraft(int days)
    {
        var quote = Draft();

        Assert.Throws<RequestValidationException>(() => quote.Send(days, UserId, null, Now));

        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Null(quote.ValidUntil);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Send_ValidityOnBoundary_IsAccepted(int days)
    {
        var quote = Draft();

        quote.Send(days, UserId, null, Now);

        Assert.Equal(Now.AddDays(days), quote.ValidUntil);
    }

    [Fact]
    public void Transition_TooLongNote_ThrowsAndKeepsStatus()
    {
        var quote = Draft();
        quote.Send(30, UserId, null, Now);

        Assert.Throws<RequestValidationException>(() => quote.Accept(UserId, new string('n', Quote.NoteMaxLength + 1), Now));

        Assert.Equal(QuoteStatus.Sent, quote.Status);
    }

    [Fact]
    public void IsOverdue_FalseWithoutValidity()
    {
        Assert.False(Draft().IsOverdue(Now.AddYears(5)));
    }

    [Fact]
    public void Accept_AfterValidityEnded_IsStillATransitionOfDomainOnly()
    {
        // The domain does not check the clock on accept; the service/expiration job does (documented behaviour).
        var quote = Draft();
        quote.Send(1, UserId, null, Now);

        quote.Accept(UserId, null, Now.AddDays(10));

        Assert.Equal(QuoteStatus.Accepted, quote.Status);
    }

    private static CreateQuoteRequest ValidRequest() => new()
    {
        CustomerId = Guid.CreateVersion7(),
        Lines = [new QuoteLineRequest { ComponentId = Guid.CreateVersion7(), Quantity = 2 }],
    };

    [Fact]
    public void Request_Valid_HasNoErrors() => Assert.Empty(DataAnnotationsValidator.Validate(ValidRequest()));

    [Fact]
    public void Request_MissingMembers_AreRequired()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateQuoteRequest());

        Assert.Contains(nameof(CreateQuoteRequest.CustomerId), errors.Keys);
        Assert.Contains(nameof(CreateQuoteRequest.Lines), errors.Keys);
    }

    [Fact]
    public void Request_NoLines_IsRejected() =>
        Assert.Contains(nameof(CreateQuoteRequest.Lines), DataAnnotationsValidator.Validate(ValidRequest() with { Lines = [] }).Keys);

    [Fact]
    public void Request_TooManyLines_IsRejected()
    {
        var line = new QuoteLineRequest { ComponentId = Guid.CreateVersion7(), Quantity = 1 };
        var request = ValidRequest() with { Lines = [.. Enumerable.Repeat(line, Quote.MaxLines + 1)] };

        Assert.Contains(nameof(CreateQuoteRequest.Lines), DataAnnotationsValidator.Validate(request).Keys);
    }

    [Fact]
    public void Request_LineWithoutComponentOrQuantity_IsRejected()
    {
        var request = ValidRequest() with { Lines = [new QuoteLineRequest { Quantity = 1 }, new QuoteLineRequest { ComponentId = Guid.CreateVersion7() }] };

        var errors = DataAnnotationsValidator.Validate(request);

        Assert.Contains("Lines[0]", errors.Keys);
        Assert.Contains("Lines[1]", errors.Keys);
    }

    [Fact]
    public void Request_NullLine_IsRejected()
    {
        var request = ValidRequest() with { Lines = [null!] };

        Assert.Contains("Lines[0]", DataAnnotationsValidator.Validate(request).Keys);
    }
}
