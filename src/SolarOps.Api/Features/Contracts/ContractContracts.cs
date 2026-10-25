using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Installations;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Payload for <c>POST /api/contracts</c>: the contract is made from this accepted quote.</summary>
public sealed record CreateContractRequest
{
    [Required]
    public Guid? QuoteId { get; init; }
}

/// <summary>Payload for <c>POST /api/contracts/{id}/sign</c>.</summary>
public sealed record SignContractRequest
{
    /// <summary>Supply point (odberné miesto) of the installation that the signature starts.</summary>
    [Required]
    public Guid? SupplyPointId { get; init; }

    /// <summary>Date of the signature; defaults to today (UTC) and must not be in the future.</summary>
    public DateOnly? SignedOn { get; init; }

    /// <summary>Name of the created installation; defaults to "Inštalácia" plus the contract number.</summary>
    [MaxLength(Installation.NameMaxLength)]
    public string? InstallationName { get; init; }
}

/// <summary>Payload for <c>POST /api/contracts/{id}/deposit/pay</c>.</summary>
public sealed record RecordDepositRequest
{
    /// <summary>Paid amount in EUR: greater than 0, at most the contract total, at most two decimals.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>Date of payment; defaults to today (UTC) and must not be in the future.</summary>
    public DateOnly? PaidOn { get; init; }
}

public sealed record ContractAuditEntryResponse(ContractAction Action, Guid PerformedBy, DateTimeOffset PerformedAt, string? Details)
{
    public static ContractAuditEntryResponse FromEntity(ContractAuditEntry e) => new(e.Action, e.PerformedBy, e.PerformedAt, e.Details);
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
    DateOnly? SignedOn,
    Guid? InstallationId,
    DepositStatus DepositStatus,
    decimal? DepositPaidAmount,
    DateOnly? DepositPaidOn,
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
        c.SignedOn,
        c.InstallationId,
        c.DepositStatus,
        c.DepositPaidAmount,
        c.DepositPaidOn,
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
