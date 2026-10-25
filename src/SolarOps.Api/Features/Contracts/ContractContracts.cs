using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Features.Components;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Payload for <c>POST /api/contracts</c>: the contract is made from this accepted quote.</summary>
public sealed record CreateContractRequest
{
    [Required]
    public Guid? QuoteId { get; init; }
}

public sealed record ContractLineResponse(
    int Position,
    Guid ComponentId,
    ComponentType ComponentType,
    string Code,
    string Name,
    decimal UnitPrice,
    decimal Quantity,
    decimal NetPrice)
{
    public static ContractLineResponse FromEntity(ContractLine l) =>
        new(l.Position, l.ComponentId, l.ComponentType, l.Code, l.Name, l.UnitPrice, l.Quantity, l.NetPrice);
}

public sealed record ContractResponse(
    Guid Id,
    string Number,
    Guid CustomerId,
    Guid QuoteId,
    ContractStatus Status,
    decimal VatRatePercent,
    string Currency,
    IReadOnlyList<ContractLineResponse> Lines,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ContractResponse FromEntity(Contract c) => new(
        c.Id,
        c.Number,
        c.CustomerId,
        c.QuoteId,
        c.Status,
        c.VatRatePercent,
        "EUR",
        c.Lines.OrderBy(l => l.Position).Select(ContractLineResponse.FromEntity).ToList(),
        c.NetTotal,
        c.VatTotal,
        c.GrossTotal,
        c.CreatedBy,
        c.CreatedAt,
        c.UpdatedAt);
}
