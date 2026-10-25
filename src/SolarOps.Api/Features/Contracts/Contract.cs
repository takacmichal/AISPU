using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Lifecycle of a contract (BPMN mapping #69). PDF and sending states follow with #80.</summary>
public enum ContractStatus
{
    Draft,
    Signed,
}

/// <summary>Deposit (zálohová platba) of a contract.</summary>
public enum DepositStatus
{
    NotPaid,
    Paid,
}

/// <summary>Audited operations on a contract.</summary>
public enum ContractAction
{
    Created,
    Signed,
    DepositPaid,
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
    private readonly List<ContractAuditEntry> _auditEntries = [];

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

    /// <summary>Date the customer signed the contract.</summary>
    public DateOnly? SignedOn { get; private set; }

    /// <summary>Installation started by the signature; realization cannot begin before the deposit is paid.</summary>
    public Guid? InstallationId { get; private set; }

    public DepositStatus DepositStatus { get; private set; }

    public decimal? DepositPaidAmount { get; private set; }

    public DateOnly? DepositPaidOn { get; private set; }

    public decimal VatRatePercent { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal VatTotal { get; private set; }

    public decimal GrossTotal { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ContractLine> Lines => _lines;

    public IReadOnlyList<ContractAuditEntry> AuditEntries => _auditEntries;

    /// <summary>Records the customer's signature and the installation it starts. Only a draft can be signed.</summary>
    public void Sign(DateOnly signedOn, Guid installationId, Guid by, DateOnly today, DateTimeOffset now)
    {
        if (Status != ContractStatus.Draft)
        {
            throw new DomainException($"Only a draft contract can be signed (the contract is {Status}).");
        }

        if (signedOn > today)
        {
            throw new RequestValidationException("SignedOn", "The signature date cannot be in the future.");
        }

        Status = ContractStatus.Signed;
        SignedOn = signedOn;
        InstallationId = installationId;
        UpdatedAt = now;
        _auditEntries.Add(ContractAuditEntry.Create(Id, ContractAction.Signed, by, now, FormattableString.Invariant($"Signed on {signedOn:yyyy-MM-dd}; installation {installationId}")));
    }

    /// <summary>Records the paid deposit. Needs a signed contract; the deposit can be paid only once.</summary>
    public void RecordDeposit(decimal amount, DateOnly paidOn, Guid by, DateOnly today, DateTimeOffset now)
    {
        if (Status != ContractStatus.Signed)
        {
            throw new DomainException("The deposit can be recorded only for a signed contract.");
        }

        if (DepositStatus == DepositStatus.Paid)
        {
            throw new DomainException("The deposit is already paid.");
        }

        var errors = new Dictionary<string, string[]>();
        if (amount <= 0 || amount > GrossTotal || decimal.Round(amount, 2) != amount)
        {
            errors["Amount"] = [FormattableString.Invariant($"Amount must be greater than 0 and at most the contract total ({GrossTotal:0.00}), with at most two decimal places.")];
        }

        if (paidOn > today)
        {
            errors["PaidOn"] = ["The payment date cannot be in the future."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        DepositStatus = DepositStatus.Paid;
        DepositPaidAmount = amount;
        DepositPaidOn = paidOn;
        UpdatedAt = now;
        _auditEntries.Add(ContractAuditEntry.Create(Id, ContractAction.DepositPaid, by, now, FormattableString.Invariant($"Deposit {amount:0.00} EUR paid on {paidOn:yyyy-MM-dd}")));
    }

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

        contract._auditEntries.Add(ContractAuditEntry.Create(contract.Id, ContractAction.Created, createdBy, now, $"Created from quote {quoteId}"));
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

/// <summary>Audit trail entry of a contract: who did what and when.</summary>
public sealed class ContractAuditEntry
{
    public const int DetailsMaxLength = 500;

    private ContractAuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid ContractId { get; private set; }

    public ContractAction Action { get; private set; }

    public Guid PerformedBy { get; private set; }

    public DateTimeOffset PerformedAt { get; private set; }

    public string? Details { get; private set; }

    internal static ContractAuditEntry Create(Guid contractId, ContractAction action, Guid by, DateTimeOffset at, string? details) => new()
    {
        Id = Guid.CreateVersion7(),
        ContractId = contractId,
        Action = action,
        PerformedBy = by,
        PerformedAt = at,
        Details = details,
    };
}
