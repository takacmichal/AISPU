using SolarOps.Api.Features.Components;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Quotes;

/// <summary>Lifecycle of a quote (BPMN mapping #69): Draft, then Sent, then Accepted, Rejected or Expired.</summary>
public enum QuoteStatus
{
    Draft,
    Sent,
    Accepted,
    Rejected,
    Expired,
}

/// <summary>Totals of a quote in EUR.</summary>
public sealed record QuoteTotals(decimal Net, decimal Vat, decimal Gross);

/// <summary>Server-side price calculation; all amounts are rounded to cents (away from zero).</summary>
public static class QuotePricing
{
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Net price of a line: unit price times quantity.</summary>
    public static decimal LineNet(decimal unitPrice, decimal quantity) => Round(unitPrice * quantity);

    /// <summary>VAT is computed once from the net total, not per line, so the totals do not drift by rounding.</summary>
    public static QuoteTotals Calculate(IEnumerable<decimal> lineNets, decimal vatRatePercent)
    {
        var net = lineNets.Sum();
        var vat = Round(net * vatRatePercent / 100m);
        return new QuoteTotals(net, vat, net + vat);
    }
}

/// <summary>Component data copied into a quote line at the time the quote is created.</summary>
public sealed record QuoteLineData(Guid ComponentId, ComponentType Type, string Code, string Name, decimal UnitPrice, decimal Quantity);

/// <summary>
/// Priced offer to a customer, built from catalog components. Component name and unit price are copied into the
/// lines, so later catalog price changes never alter an issued quote. Totals are always computed by the server.
/// </summary>
public sealed class Quote
{
    public const decimal DefaultVatRatePercent = 23m;
    public const int NoteMaxLength = 2000;
    public const int MaxLines = 200;
    public const decimal MaxQuantity = 10_000m;
    public const int MaxValidityDays = 365;

    private static readonly IReadOnlyDictionary<QuoteStatus, QuoteStatus[]> AllowedTransitionMap =
        new Dictionary<QuoteStatus, QuoteStatus[]>
        {
            [QuoteStatus.Draft] = [QuoteStatus.Sent],
            [QuoteStatus.Sent] = [QuoteStatus.Accepted, QuoteStatus.Rejected, QuoteStatus.Expired],
            [QuoteStatus.Accepted] = [],
            [QuoteStatus.Rejected] = [],
            [QuoteStatus.Expired] = [],
        };

    private readonly List<QuoteLine> _lines = [];
    private readonly List<QuoteStateChange> _stateChanges = [];

    private Quote()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Customer of another module, referenced by id only.</summary>
    public Guid CustomerId { get; private set; }

    public QuoteStatus Status { get; private set; }

    public decimal VatRatePercent { get; private set; }

    public string? Note { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal VatTotal { get; private set; }

    public decimal GrossTotal { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<QuoteLine> Lines => _lines;

    /// <summary>End of the validity period; set when the quote is sent. A sent quote past it expires.</summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    public IReadOnlyList<QuoteStateChange> StateChanges => _stateChanges;

    public IReadOnlyList<QuoteStatus> AllowedTransitions => AllowedTransitionMap[Status];

    /// <summary>True for a sent quote whose validity period has ended but that was not marked Expired yet.</summary>
    public bool IsOverdue(DateTimeOffset now) => Status == QuoteStatus.Sent && ValidUntil is { } until && now >= until;

    /// <summary>Sends the quote to the customer and starts its validity period.</summary>
    public void Send(int validityDays, Guid by, string? note, DateTimeOffset now)
    {
        if (validityDays is < 1 or > MaxValidityDays)
        {
            throw new RequestValidationException("ValidityDays", $"Validity must be between 1 and {MaxValidityDays} days.");
        }

        TransitionTo(QuoteStatus.Sent, by, note, now);
        ValidUntil = now.AddDays(validityDays);
    }

    public void Accept(Guid by, string? note, DateTimeOffset now) => TransitionTo(QuoteStatus.Accepted, by, note, now);

    public void Reject(Guid by, string? note, DateTimeOffset now) => TransitionTo(QuoteStatus.Rejected, by, note, now);

    /// <summary>Expires a sent quote past its validity (done by the system, so there is no user).</summary>
    public void Expire(DateTimeOffset now) => TransitionTo(QuoteStatus.Expired, null, "Validity period ended.", now);

    private void TransitionTo(QuoteStatus target, Guid? by, string? note, DateTimeOffset now)
    {
        if (note?.Length > NoteMaxLength)
        {
            throw new RequestValidationException("Note", $"Note is at most {NoteMaxLength} characters.");
        }

        if (!AllowedTransitionMap[Status].Contains(target))
        {
            throw new DomainException($"Quote cannot move from {Status} to {target}.");
        }

        var from = Status;
        Status = target;
        UpdatedAt = now;
        _stateChanges.Add(QuoteStateChange.Create(Id, from, target, by, note, now));
    }

    /// <summary>Creates a quote in state Draft. Throws <see cref="RequestValidationException"/> on invalid lines or VAT rate.</summary>
    public static Quote Create(
        Guid customerId,
        IReadOnlyList<QuoteLineData> lines,
        decimal vatRatePercent,
        string? note,
        Guid createdBy,
        DateTimeOffset now)
    {
        var errors = Validate(lines, vatRatePercent, note);
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var quote = new Quote
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Status = QuoteStatus.Draft,
            VatRatePercent = vatRatePercent,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };

        for (var i = 0; i < lines.Count; i++)
        {
            quote._lines.Add(new QuoteLine(quote.Id, i + 1, lines[i]));
        }

        var totals = QuotePricing.Calculate(quote._lines.Select(l => l.NetPrice), vatRatePercent);
        quote.NetTotal = totals.Net;
        quote.VatTotal = totals.Vat;
        quote.GrossTotal = totals.Gross;
        quote._stateChanges.Add(QuoteStateChange.Create(quote.Id, null, QuoteStatus.Draft, createdBy, null, now));
        return quote;
    }

    private static Dictionary<string, string[]> Validate(IReadOnlyList<QuoteLineData> lines, decimal vatRatePercent, string? note)
    {
        var errors = new Dictionary<string, string[]>();

        if (lines.Count is 0 or > MaxLines)
        {
            errors["Lines"] = [$"A quote needs 1 to {MaxLines} lines."];
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var quantity = lines[i].Quantity;
            var key = $"Lines[{i}].Quantity";
            if (quantity <= 0 || quantity > MaxQuantity)
            {
                errors[key] = [$"Quantity must be greater than 0 and at most {MaxQuantity:0}."];
            }
            else if (decimal.Round(quantity, 2) != quantity)
            {
                errors[key] = ["Quantity has at most two decimal places."];
            }
            else if (lines[i].Type != ComponentType.Labor && quantity != decimal.Truncate(quantity))
            {
                errors[key] = ["Panels and inverters are sold in whole pieces."];
            }
        }

        if (vatRatePercent is < 0 or > 100 || decimal.Round(vatRatePercent, 2) != vatRatePercent)
        {
            errors["VatRatePercent"] = ["VAT rate must be between 0 and 100 with at most two decimal places."];
        }

        if (note?.Length > NoteMaxLength)
        {
            errors["Note"] = [$"Note is at most {NoteMaxLength} characters."];
        }

        return errors;
    }
}

/// <summary>One priced line of a quote; a snapshot of the catalog component at quote time.</summary>
public sealed class QuoteLine
{
    private QuoteLine()
    {
        Code = Name = string.Empty;
    }

    internal QuoteLine(Guid quoteId, int position, QuoteLineData data)
    {
        Id = Guid.CreateVersion7();
        QuoteId = quoteId;
        Position = position;
        ComponentId = data.ComponentId;
        ComponentType = data.Type;
        Code = data.Code;
        Name = data.Name;
        UnitPrice = data.UnitPrice;
        Quantity = data.Quantity;
        NetPrice = QuotePricing.LineNet(data.UnitPrice, data.Quantity);
    }

    public Guid Id { get; private set; }

    public Guid QuoteId { get; private set; }

    /// <summary>1-based order of the line in the quote.</summary>
    public int Position { get; private set; }

    public Guid ComponentId { get; private set; }

    public ComponentType ComponentType { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Unit price times quantity, rounded to cents.</summary>
    public decimal NetPrice { get; private set; }
}

/// <summary>History entry of a quote: who changed the status and when. <see cref="ChangedBy"/> is null for system changes (expiry).</summary>
public sealed class QuoteStateChange
{
    private QuoteStateChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid QuoteId { get; private set; }

    /// <summary>Null for the creation entry.</summary>
    public QuoteStatus? FromStatus { get; private set; }

    public QuoteStatus ToStatus { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Note { get; private set; }

    internal static QuoteStateChange Create(Guid quoteId, QuoteStatus? from, QuoteStatus to, Guid? changedBy, string? note, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        QuoteId = quoteId,
        FromStatus = from,
        ToStatus = to,
        ChangedBy = changedBy,
        ChangedAt = at,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
    };
}
