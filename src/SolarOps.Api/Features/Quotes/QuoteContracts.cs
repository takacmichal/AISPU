using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Features.Components;

namespace SolarOps.Api.Features.Quotes;

public sealed record QuoteLineRequest
{
    [Required]
    public Guid? ComponentId { get; init; }

    /// <summary>Pieces for panels and inverters (whole numbers), hours for labor.</summary>
    [Required]
    public decimal? Quantity { get; init; }
}

/// <summary>Payload for <c>POST /api/quotes</c>. Prices come from the catalog; the client never sends them.</summary>
public sealed record CreateQuoteRequest : IValidatableObject
{
    [Required]
    public Guid? CustomerId { get; init; }

    [Required]
    public IReadOnlyList<QuoteLineRequest>? Lines { get; init; }

    /// <summary>VAT rate in percent; defaults to 23.</summary>
    public decimal? VatRatePercent { get; init; }

    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines is null)
        {
            yield break;
        }

        if (Lines.Count is 0 or > Quote.MaxLines)
        {
            yield return new ValidationResult($"A quote needs 1 to {Quote.MaxLines} lines.", [nameof(Lines)]);
        }

        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i] is null)
            {
                yield return new ValidationResult("Line must not be null.", [$"Lines[{i}]"]);
            }
            else if (Lines[i].ComponentId is null || Lines[i].Quantity is null)
            {
                yield return new ValidationResult("ComponentId and Quantity are required.", [$"Lines[{i}]"]);
            }
        }
    }
}

public sealed record QuoteLineResponse(
    int Position,
    Guid ComponentId,
    ComponentType ComponentType,
    string Code,
    string Name,
    decimal UnitPrice,
    decimal Quantity,
    decimal NetPrice)
{
    public static QuoteLineResponse FromEntity(QuoteLine l) =>
        new(l.Position, l.ComponentId, l.ComponentType, l.Code, l.Name, l.UnitPrice, l.Quantity, l.NetPrice);
}

public sealed record QuoteResponse(
    Guid Id,
    Guid CustomerId,
    QuoteStatus Status,
    IReadOnlyList<QuoteStatus> AllowedTransitions,
    DateTimeOffset? ValidUntil,
    decimal VatRatePercent,
    string Currency,
    string? Note,
    IReadOnlyList<QuoteLineResponse> Lines,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static QuoteResponse FromEntity(Quote q) => new(
        q.Id,
        q.CustomerId,
        q.Status,
        q.AllowedTransitions,
        q.ValidUntil,
        q.VatRatePercent,
        "EUR",
        q.Note,
        q.Lines.OrderBy(l => l.Position).Select(QuoteLineResponse.FromEntity).ToList(),
        q.NetTotal,
        q.VatTotal,
        q.GrossTotal,
        q.CreatedBy,
        q.CreatedAt,
        q.UpdatedAt);
}

/// <summary>Optional body of <c>POST /api/quotes/{id}/send</c>.</summary>
public sealed record SendQuoteRequest
{
    /// <summary>How long the quote is valid (1 to 365 days); defaults to <c>Quotes:DefaultValidityDays</c> (30).</summary>
    public int? ValidityDays { get; init; }

    public string? Note { get; init; }
}

/// <summary>Optional body of <c>accept</c> and <c>reject</c>.</summary>
public sealed record QuoteNoteRequest
{
    public string? Note { get; init; }
}

public sealed record QuoteStateChangeResponse(
    QuoteStatus? FromStatus,
    QuoteStatus ToStatus,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt,
    string? Note)
{
    public static QuoteStateChangeResponse FromEntity(QuoteStateChange c) =>
        new(c.FromStatus, c.ToStatus, c.ChangedBy, c.ChangedAt, c.Note);
}
