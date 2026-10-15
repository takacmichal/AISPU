using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Installations;

public sealed class InstallationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static Installation NewInstallation() =>
        Installation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), " Rodinný dom ", 9.5m, null, User, Now);

    private static Installation ReadyForMounting()
    {
        var installation = NewInstallation();
        installation.Schedule(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), Now);
        installation.AssignTechnicians([Guid.CreateVersion7()], Now);
        installation.OrderComponents(User, Now);
        installation.ReceiveComponents(User, Now);
        return installation;
    }

    [Fact]
    public void Create_StartsPlanned_WithHistoryEntry()
    {
        var installation = NewInstallation();

        Assert.Equal(InstallationState.Planned, installation.State);
        Assert.Equal("Rodinný dom", installation.Name);
        var change = Assert.Single(installation.StateChanges);
        Assert.Null(change.FromState);
        Assert.Equal(InstallationState.Planned, change.ToState);
        Assert.Equal([InstallationState.ComponentsOrdered], installation.AllowedTransitions);
    }

    [Fact]
    public void HappyPath_ReachesHandedOver_AndRecordsEveryStep()
    {
        var installation = ReadyForMounting();

        installation.StartMounting(User, Now);
        installation.CompleteMounting(User, Now);
        installation.StartInspection(User, Now);
        installation.RecordInspection(passed: true, User, "OK", Now);
        installation.Commission(User, Now);
        installation.HandOver(User, Now);

        Assert.Equal(InstallationState.HandedOver, installation.State);
        Assert.Empty(installation.AllowedTransitions);
        Assert.Equal(9, installation.StateChanges.Count);
    }

    [Fact]
    public void FailedInspection_AllowsReworkBackToMounting()
    {
        var installation = ReadyForMounting();
        installation.StartMounting(User, Now);
        installation.CompleteMounting(User, Now);
        installation.StartInspection(User, Now);

        installation.RecordInspection(passed: false, User, "Chýba uzemnenie", Now);
        Assert.Equal(InstallationState.InspectionFailed, installation.State);

        installation.Rework(User, null, Now);
        Assert.Equal(InstallationState.Mounting, installation.State);
    }

    [Theory]
    [InlineData(InstallationState.Planned, InstallationState.Mounting)]
    [InlineData(InstallationState.Planned, InstallationState.HandedOver)]
    [InlineData(InstallationState.Planned, InstallationState.Planned)]
    [InlineData(InstallationState.ComponentsOrdered, InstallationState.Planned)]
    [InlineData(InstallationState.Inspection, InstallationState.Commissioned)]
    [InlineData(InstallationState.InspectionFailed, InstallationState.InspectionPassed)]
    [InlineData(InstallationState.HandedOver, InstallationState.Planned)]
    public void CanTransition_RejectsInvalidPairs(InstallationState from, InstallationState to)
    {
        Assert.False(Installation.CanTransition(from, to));
    }

    [Fact]
    public void TransitionTo_InvalidTarget_ThrowsDomainException_AndKeepsState()
    {
        var installation = NewInstallation();

        var ex = Assert.Throws<DomainException>(() => installation.TransitionTo(InstallationState.Commissioned, User, null, Now));

        Assert.Contains("Planned", ex.Message);
        Assert.Equal(InstallationState.Planned, installation.State);
        Assert.Single(installation.StateChanges);
    }

    [Fact]
    public void StartMounting_WithoutScheduleOrTechnicians_Throws()
    {
        var installation = NewInstallation();
        installation.OrderComponents(User, Now);
        installation.ReceiveComponents(User, Now);

        Assert.Throws<DomainException>(() => installation.StartMounting(User, Now));

        installation.Schedule(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), Now);
        Assert.Throws<DomainException>(() => installation.StartMounting(User, Now));
    }

    [Fact]
    public void Schedule_EndBeforeStart_Throws()
    {
        var installation = NewInstallation();

        Assert.Throws<DomainException>(() => installation.Schedule(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 2), Now));
    }

    [Fact]
    public void AssignTechnicians_RemovesDuplicates()
    {
        var installation = NewInstallation();
        var technician = Guid.CreateVersion7();

        installation.AssignTechnicians([technician, technician], Now);

        Assert.Equal([technician], installation.TechnicianIds);
    }

    [Fact]
    public void HandedOver_IsReadOnly()
    {
        var installation = ReadyForMounting();
        foreach (var state in new[]
                 {
                     InstallationState.Mounting, InstallationState.MountingDone, InstallationState.Inspection,
                     InstallationState.InspectionPassed, InstallationState.Commissioned, InstallationState.HandedOver,
                 })
        {
            installation.TransitionTo(state, User, null, Now);
        }

        Assert.Throws<DomainException>(() => installation.UpdateDetails("x", null, null, Now));
        Assert.Throws<DomainException>(() => installation.AssignTechnicians([], Now));
        Assert.Throws<DomainException>(() => installation.Schedule(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), Now));
    }
}
