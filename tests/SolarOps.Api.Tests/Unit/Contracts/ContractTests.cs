using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Contracts;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Contracts;

public sealed class ContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 25);
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static Contract Draft(decimal gross = 1000m) => Contract.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        23m,
        [new ContractLineData(Guid.CreateVersion7(), ComponentType.Panel, "PAN", "Panel", 100m, 2m, 200m)],
        new QuoteTotals(gross / 1.23m, gross - (gross / 1.23m), gross),
        UserId,
        Now);

    [Fact]
    public void Create_IsDraftWithUnpaidDeposit_AndAuditEntry()
    {
        var contract = Draft();

        Assert.Equal(ContractStatus.Draft, contract.Status);
        Assert.Equal(DepositStatus.NotPaid, contract.DepositStatus);
        Assert.Equal(ContractAction.Created, Assert.Single(contract.AuditEntries).Action);
    }

    [Fact]
    public void Create_WithoutLines_Throws() =>
        Assert.Throws<DomainException>(() => Contract.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 23m, [], new QuoteTotals(0, 0, 0), UserId, Now));

    [Fact]
    public void Sign_MovesToSigned_AndRemembersInstallationAndAudit()
    {
        var contract = Draft();
        var installationId = Guid.CreateVersion7();

        contract.Sign(Today, installationId, UserId, Today, Now);

        Assert.Equal(ContractStatus.Signed, contract.Status);
        Assert.Equal(Today, contract.SignedOn);
        Assert.Equal(installationId, contract.InstallationId);
        Assert.Equal([ContractAction.Created, ContractAction.Signed], contract.AuditEntries.Select(e => e.Action));
    }

    [Fact]
    public void Sign_Twice_OrWithFutureDate_Throws()
    {
        var contract = Draft();
        Assert.Throws<RequestValidationException>(() => contract.Sign(Today.AddDays(1), Guid.CreateVersion7(), UserId, Today, Now));
        Assert.Equal(ContractStatus.Draft, contract.Status);

        contract.Sign(Today, Guid.CreateVersion7(), UserId, Today, Now);
        Assert.Throws<DomainException>(() => contract.Sign(Today, Guid.CreateVersion7(), UserId, Today, Now));
    }

    [Fact]
    public void RecordDeposit_RequiresSignedContract_AndWorksOnce()
    {
        var contract = Draft();
        Assert.Throws<DomainException>(() => contract.RecordDeposit(100m, Today, UserId, Today, Now));

        contract.Sign(Today, Guid.CreateVersion7(), UserId, Today, Now);
        contract.RecordDeposit(100m, Today, UserId, Today, Now);

        Assert.Equal(DepositStatus.Paid, contract.DepositStatus);
        Assert.Equal(100m, contract.DepositPaidAmount);
        Assert.Equal(ContractAction.DepositPaid, contract.AuditEntries[^1].Action);
        Assert.Throws<DomainException>(() => contract.RecordDeposit(100m, Today, UserId, Today, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000.01)]
    [InlineData(10.005)]
    public void RecordDeposit_InvalidAmount_Throws_AndKeepsDepositUnpaid(double amount)
    {
        var contract = Draft(1000m);
        contract.Sign(Today, Guid.CreateVersion7(), UserId, Today, Now);

        var ex = Assert.Throws<RequestValidationException>(() => contract.RecordDeposit((decimal)amount, Today, UserId, Today, Now));

        Assert.Contains("Amount", ex.Errors.Keys);
        Assert.Equal(DepositStatus.NotPaid, contract.DepositStatus);
        Assert.Null(contract.DepositPaidAmount);
    }

    [Fact]
    public void RecordDeposit_FullContractAmountIsAllowed()
    {
        var contract = Draft(1000m);
        contract.Sign(Today, Guid.CreateVersion7(), UserId, Today, Now);

        contract.RecordDeposit(1000m, Today, UserId, Today, Now);

        Assert.Equal(DepositStatus.Paid, contract.DepositStatus);
    }
}
