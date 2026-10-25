using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Lifecycle of a contract (BPMN mapping #69). More states follow with the PDF (#80) and signature (#81) stories.</summary>
public enum ContractStatus
{
    Draft,
}

/// <summary>Quote line data copied into a contract line.</summary>
public sealed record ContractLineData(
    Guid ComponentId,
    ComponentType ComponentType,
    string Code,
    string Name,
    decimal UnitPrice,
    decimal Quantity,
    decimal NetPrice);

/// <summary>
/// Supply contract made from an accepted quote. Customer, lines and prices are copied from the quote, so later
/// changes elsewhere never alter the contract. Other modules are referenced by id only.
/// </summary>
public sealed class Contract
{
    private readonly List<ContractLine> _lines = [];

    private Contract()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Number from a database sequence, so it is unique even for concurrent requests.</summary>
    public int Sequence { get; private set; }

    /// <summary>Human readable unique number, e.g. ZM-2026-000042.</summary>
    public string Number => $"ZM-{CreatedAt.Year}-{Sequence:000000}";

    public Guid CustomerId { get; private set; }

    /// <summary>The accepted quote the contract was made from; one contract per quote.</summary>
    public Guid QuoteId { get; private set; }

    public ContractStatus Status { get; private set; }

    public decimal VatRatePercent { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal VatTotal { get; private set; }

    public decimal GrossTotal { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ContractLine> Lines => _lines;

    /// <summary>Creates a contract in state Draft from the data of an accepted quote.</summary>
    public static Contract Create(
        Guid customerId,
        Guid quoteId,
        decimal vatRatePercent,
        IReadOnlyList<ContractLineData> lines,
        QuoteTotals totals,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (lines.Count == 0)
        {
            throw new DomainException("A contract needs at least one line.");
        }

        var contract = new Contract
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            QuoteId = quoteId,
            Status = ContractStatus.Draft,
            VatRatePercent = vatRatePercent,
            NetTotal = totals.Net,
            VatTotal = totals.Vat,
            GrossTotal = totals.Gross,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };

        for (var i = 0; i < lines.Count; i++)
        {
            contract._lines.Add(new ContractLine(contract.Id, i + 1, lines[i]));
        }

        return contract;
    }
}

/// <summary>One line of a contract; a copy of the quote line.</summary>
public sealed class ContractLine
{
    private ContractLine()
    {
        Code = Name = string.Empty;
    }

    internal ContractLine(Guid contractId, int position, ContractLineData data)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        Position = position;
        ComponentId = data.ComponentId;
        ComponentType = data.ComponentType;
        Code = data.Code;
        Name = data.Name;
        UnitPrice = data.UnitPrice;
        Quantity = data.Quantity;
        NetPrice = data.NetPrice;
    }

    public Guid Id { get; private set; }

    public Guid ContractId { get; private set; }

    public int Position { get; private set; }

    public Guid ComponentId { get; private set; }

    public ComponentType ComponentType { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal NetPrice { get; private set; }
}
