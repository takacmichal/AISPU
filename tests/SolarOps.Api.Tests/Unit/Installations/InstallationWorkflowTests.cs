using System.Security.Cryptography;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Installations;

/// <summary>Scheduling rules (#88), assembly checklist (#90), revision report (#91) and commissioning (#92).</summary>
public sealed class InstallationWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly Guid User = Guid.CreateVersion7();

    private static Installation NewInstallation() =>
        Installation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Rodinný dom", 9.5m, null, User, Now);

    private static Installation InState(InstallationState state)
    {
        var installation = NewInstallation();
        InstallationLifecycle.WalkTo(installation, state, Now);
        return installation;
    }

    [Fact]
    public void Schedule_StartInThePast_Throws()
    {
        var installation = NewInstallation();

        var ex = Assert.Throws<DomainException>(() => installation.Schedule(Today.AddDays(-1), Today.AddDays(1), Now));

        Assert.Contains("past", ex.Message);
        Assert.Null(installation.ScheduledStart);
    }

    [Fact]
    public void Schedule_Today_IsAllowed()
    {
        var installation = NewInstallation();

        installation.Schedule(Today, Today, Now);

        Assert.Equal(Today, installation.ScheduledStart);
    }

    [Fact]
    public void Schedule_ExtendingAnOngoingWindow_KeepsThePastStart_ButEndMustNotBeInThePast()
    {
        var installation = NewInstallation();
        installation.Schedule(Today, Today.AddDays(1), Now);
        var later = Now.AddDays(3);

        installation.Schedule(Today, Today.AddDays(5), later);
        Assert.Equal(Today.AddDays(5), installation.ScheduledEnd);

        Assert.Throws<DomainException>(() => installation.Schedule(Today, Today.AddDays(2), later));
        Assert.Throws<DomainException>(() => installation.Schedule(Today.AddDays(1), Today.AddDays(5), later));
    }

    [Fact]
    public void ChecklistTemplate_HasAtLeastEightSteps_WithUniqueCodes()
    {
        Assert.True(ChecklistTemplate.Steps.Count >= 8);
        Assert.Equal(ChecklistTemplate.Steps.Count, ChecklistTemplate.Steps.Select(s => s.Code).Distinct().Count());
        Assert.True(ChecklistTemplate.Steps.Count(s => s.Required) >= 8);
        Assert.Equal(Enumerable.Range(1, ChecklistTemplate.Steps.Count), ChecklistTemplate.Steps.Select(s => s.Position));
        Assert.All(ChecklistTemplate.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
    }

    [Fact]
    public void SetChecklistStep_OutsideMounting_Throws()
    {
        var installation = NewInstallation();

        Assert.Throws<DomainException>(() => installation.SetChecklistStep(ChecklistTemplate.Steps[0].Code, true, null, User, Now));
    }

    [Fact]
    public void SetChecklistStep_UnknownStep_ThrowsNotFound()
    {
        var installation = InState(InstallationState.Mounting);

        Assert.Throws<NotFoundException>(() => installation.SetChecklistStep("NoSuchStep", true, null, User, Now));
    }

    [Fact]
    public void SetChecklistStep_RecordsWhoAndWhen_AndCanBeUnchecked()
    {
        var installation = InState(InstallationState.Mounting);
        var code = ChecklistTemplate.Steps[0].Code;
        var technician = Guid.CreateVersion7();

        installation.SetChecklistStep(code, true, " Lešenie postavené ", technician, Now.AddHours(1));

        var item = Assert.Single(installation.ChecklistItems);
        Assert.Equal(code, item.StepCode);
        Assert.True(item.Done);
        Assert.Equal("Lešenie postavené", item.Note);
        Assert.Equal(technician, item.ChangedBy);
        Assert.Equal(Now.AddHours(1), item.ChangedAt);

        installation.SetChecklistStep(code, false, null, User, Now.AddHours(2));

        item = Assert.Single(installation.ChecklistItems);
        Assert.False(item.Done);
        Assert.Null(item.Note);
        Assert.Equal(Now.AddHours(2), item.ChangedAt);
    }

    [Fact]
    public void SetChecklistStep_IsCaseInsensitive_OnTheCode()
    {
        var installation = InState(InstallationState.Mounting);

        installation.SetChecklistStep(ChecklistTemplate.Steps[0].Code.ToLowerInvariant(), true, null, User, Now);

        Assert.Equal(ChecklistTemplate.Steps[0].Code, Assert.Single(installation.ChecklistItems).StepCode);
    }

    [Fact]
    public void CompleteMounting_WithoutAllRequiredSteps_Throws_AndListsMissingSteps()
    {
        var installation = InState(InstallationState.Mounting);
        var required = ChecklistTemplate.Steps.Where(s => s.Required).ToList();
        foreach (var step in required.Skip(1))
        {
            installation.SetChecklistStep(step.Code, true, null, User, Now);
        }

        var ex = Assert.Throws<DomainException>(() => installation.CompleteMounting(User, Now));

        Assert.Contains(required[0].Code, ex.Message);
        Assert.Equal(InstallationState.Mounting, installation.State);
        Assert.Equal([required[0].Code], installation.MissingRequiredChecklistSteps);
    }

    [Fact]
    public void CompleteMounting_OptionalStepsAreNotNeeded()
    {
        var installation = InState(InstallationState.Mounting);

        InstallationLifecycle.CompleteChecklist(installation, User, Now);
        installation.CompleteMounting(User, Now);

        Assert.Equal(InstallationState.MountingDone, installation.State);
        Assert.True(installation.IsChecklistComplete);
    }

    [Theory]
    [InlineData(InstallationState.Inspection, InstallationState.InspectionPassed)]
    [InlineData(InstallationState.Inspection, InstallationState.InspectionFailed)]
    [InlineData(InstallationState.InspectionPassed, InstallationState.Commissioned)]
    public void TransitionTo_StatesWithTheirOwnStep_AreRejected(InstallationState from, InstallationState target)
    {
        var installation = InState(from);

        Assert.Throws<DomainException>(() => installation.TransitionTo(target, User, null, Now));
        Assert.Equal(from, installation.State);
    }

    [Fact]
    public void RecordRevision_OutsideInspection_Throws()
    {
        var installation = InState(InstallationState.MountingDone);

        Assert.Throws<DomainException>(() =>
            installation.RecordRevision(InstallationLifecycle.NewReport(installation, RevisionResult.Passed, Now), User, Now));
    }

    [Fact]
    public void RecordRevision_ForAnotherInstallation_Throws()
    {
        var installation = InState(InstallationState.Inspection);
        var other = NewInstallation();

        Assert.Throws<DomainException>(() =>
            installation.RecordRevision(InstallationLifecycle.NewReport(other, RevisionResult.Passed, Now), User, Now));
    }

    [Fact]
    public void RecordRevision_Passed_MovesToInspectionPassed_AndRemembersValidity()
    {
        var installation = InState(InstallationState.Inspection);
        var report = InstallationLifecycle.NewReport(installation, RevisionResult.Passed, Now, Today.AddYears(4));

        installation.RecordRevision(report, User, Now);

        Assert.Equal(InstallationState.InspectionPassed, installation.State);
        Assert.Equal(report.Id, installation.LastRevisionReportId);
        Assert.Equal(RevisionResult.Passed, installation.LastRevisionResult);
        Assert.Equal(Today.AddYears(4), installation.LastRevisionValidUntil);
    }

    [Fact]
    public void RecordRevision_Failed_ReturnsToRepairs_AndChecklistIsEditableAgain()
    {
        var installation = InState(InstallationState.Inspection);

        installation.RecordRevision(InstallationLifecycle.NewReport(installation, RevisionResult.Failed, Now), User, Now);

        Assert.Equal(InstallationState.InspectionFailed, installation.State);
        Assert.Equal("Chýba uzemnenie", installation.StateChanges[^1].Note);
        Assert.Equal([InstallationState.Mounting], installation.AllowedTransitions);

        installation.Rework(User, "Doplniť uzemnenie", Now);
        installation.SetChecklistStep(ChecklistTemplate.Steps[0].Code, false, "Znova skontrolovať", User, Now);
        Assert.Throws<DomainException>(() => installation.CompleteMounting(User, Now));
    }

    [Fact]
    public void RevisionReport_Create_StoresHashAndSize_AndTrimsText()
    {
        var installation = NewInstallation();

        var report = RevisionReport.Create(
            installation.Id, Today, "  Ing. Revízny ", null, RevisionResult.Passed, Today.AddYears(4), null,
            "  ../../etc/revizia.pdf ", "application/pdf", InstallationLifecycle.PdfBytes, User, Now);

        Assert.Equal("Ing. Revízny", report.InspectorName);
        Assert.Equal("revizia.pdf", report.FileName);
        Assert.Equal(InstallationLifecycle.PdfBytes.Length, report.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(InstallationLifecycle.PdfBytes)), report.Sha256);
    }

    [Fact]
    public void Commission_WithoutApprovedConnectionRequest_Throws()
    {
        var installation = InState(InstallationState.InspectionPassed);

        var ex = Assert.Throws<DomainException>(() =>
            installation.Commission(Today, "Peter Zákazník", null, connectionApproved: false, User, Now));

        Assert.Contains("connection request", ex.Message);
        Assert.Equal(InstallationState.InspectionPassed, installation.State);
        Assert.False(installation.MonitoringEnabled);
    }

    [Fact]
    public void Commission_AfterRevisionExpired_Throws()
    {
        var installation = InState(InstallationState.Inspection);
        installation.RecordRevision(InstallationLifecycle.NewReport(installation, RevisionResult.Passed, Now, Today.AddDays(10)), User, Now);

        Assert.Throws<DomainException>(() =>
            installation.Commission(Today.AddDays(11), "Peter Zákazník", null, connectionApproved: true, User, Now.AddDays(11)));
    }

    [Fact]
    public void Commission_BeforeInspectionPassed_Throws()
    {
        var installation = InState(InstallationState.MountingDone);

        Assert.Throws<DomainException>(() =>
            installation.Commission(Today, "Peter Zákazník", null, connectionApproved: true, User, Now));
    }

    [Fact]
    public void Commission_InTheFuture_Throws()
    {
        var installation = InState(InstallationState.InspectionPassed);

        Assert.Throws<DomainException>(() =>
            installation.Commission(Today.AddDays(1), "Peter Zákazník", null, connectionApproved: true, User, Now));
    }

    [Fact]
    public void Commission_RecordsProtocol_AndEnablesMonitoring()
    {
        var installation = InState(InstallationState.InspectionPassed);
        var technician = Guid.CreateVersion7();

        installation.Commission(Today, " Peter Zákazník ", "Výroba overená", connectionApproved: true, technician, Now);

        Assert.Equal(InstallationState.Commissioned, installation.State);
        Assert.Equal(Today, installation.CommissionedOn);
        Assert.Equal("Peter Zákazník", installation.HandoverAcceptedBy);
        Assert.Equal("Výroba overená", installation.CommissioningNote);
        Assert.Equal(technician, installation.CommissionedBy);
        Assert.True(installation.MonitoringEnabled);
        Assert.Equal(Now, installation.MonitoringEnabledAt);
        Assert.Equal([InstallationState.HandedOver], installation.AllowedTransitions);
    }
}
