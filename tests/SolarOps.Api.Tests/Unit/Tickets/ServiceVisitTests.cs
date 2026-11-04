using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Tickets;

/// <summary>Visit lifecycle, protocol and the ticket methods driven by visits (#124 to #129).</summary>
public sealed class ServiceVisitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VisitStart = Now.AddDays(2);
    private static readonly Guid TicketId = Guid.CreateVersion7();
    private static readonly Guid TechnicianId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static ServiceVisit NewVisit(int minutes = 90) =>
        ServiceVisit.Plan(TicketId, TechnicianId, VisitStart, minutes, UserId, Now);

    private static ServiceVisit InProgressVisit()
    {
        var visit = NewVisit();
        visit.Start(UserId, VisitStart);
        return visit;
    }

    private static ServiceTicket TicketIn(params ServiceTicketState[] path)
    {
        var ticket = ServiceTicket.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "t", "d", ServiceTicketPriority.Medium,
            ServiceTicketOrigin.Manual, [], UserId, Now);
        foreach (var step in path)
        {
            ticket.TransitionTo(step, UserId, null, Now);
        }

        return ticket;
    }

    private static ServiceTicket Remote() => TicketIn(ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis);

    [Fact]
    public void Plan_SetsWindowFromStartAndEstimate_AndStartsPlanned()
    {
        var visit = NewVisit(90);

        Assert.Equal(ServiceVisitState.Planned, visit.State);
        Assert.Equal(VisitStart, visit.PlannedStart);
        Assert.Equal(VisitStart.AddMinutes(90), visit.PlannedEnd);
        Assert.Equal(90, visit.EstimatedMinutes);
        Assert.Equal(TechnicianId, visit.TechnicianId);
    }

    [Fact]
    public void Plan_NormalizesOffsetToUtc()
    {
        var local = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(1));

        var visit = ServiceVisit.Plan(TicketId, TechnicianId, local, 60, UserId, Now);

        Assert.Equal(TimeSpan.Zero, visit.PlannedStart.Offset);
        Assert.Equal(local, visit.PlannedStart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(721)]
    public void Plan_RejectsEstimateOutsideRange(int minutes) =>
        Assert.Throws<RequestValidationException>(() => NewVisit(minutes));

    [Fact]
    public void Plan_RejectsStartInThePast() =>
        Assert.Throws<RequestValidationException>(() => ServiceVisit.Plan(TicketId, TechnicianId, Now.AddMinutes(-1), 60, UserId, Now));

    [Fact]
    public void Reschedule_ChangesWindow_AndKeepsReasonInTheLog()
    {
        var visit = NewVisit();

        visit.Reschedule(VisitStart.AddDays(1), 120, " zákazník nebol doma ", UserId, Now);

        Assert.Equal(VisitStart.AddDays(1), visit.PlannedStart);
        Assert.Equal(VisitStart.AddDays(1).AddHours(2), visit.PlannedEnd);
        var change = Assert.Single(visit.Reschedules);
        Assert.Equal(VisitStart, change.PreviousStart);
        Assert.Equal("zákazník nebol doma", change.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Reschedule_RequiresReason(string? reason)
    {
        var visit = NewVisit();

        Assert.Throws<RequestValidationException>(() => visit.Reschedule(VisitStart.AddDays(1), 60, reason, UserId, Now));
        Assert.Empty(visit.Reschedules);
    }

    [Fact]
    public void Reschedule_AfterStart_Throws409()
    {
        var visit = InProgressVisit();

        Assert.Throws<DomainException>(() => visit.Reschedule(VisitStart.AddDays(1), 60, "x", UserId, Now));
    }

    [Fact]
    public void Start_MovesPlannedToInProgress_AndOnlyOnce()
    {
        var visit = NewVisit();

        visit.Start(UserId, VisitStart);

        Assert.Equal(ServiceVisitState.InProgress, visit.State);
        Assert.Equal(VisitStart, visit.StartedAt);
        Assert.Throws<DomainException>(() => visit.Start(UserId, VisitStart));
    }

    [Fact]
    public void Report_WritesProtocol_AndMovesToDone()
    {
        var visit = InProgressVisit();

        visit.Report(VisitCause.InverterFault, " Chyba DC ", " Výmena poistky ", VisitStart, VisitStart.AddHours(1), UserId, VisitStart.AddHours(2));

        Assert.Equal(ServiceVisitState.Done, visit.State);
        Assert.Equal(VisitCause.InverterFault, visit.Cause);
        Assert.Equal("Chyba DC", visit.Findings);
        Assert.Equal("Výmena poistky", visit.WorkDone);
        Assert.Equal(VisitStart, visit.ArrivedAt);
        Assert.Equal(VisitStart.AddHours(1), visit.DepartedAt);
        Assert.Equal(VisitStart.AddHours(2), visit.ReportedAt);
    }

    [Fact]
    public void Report_CannotBeChangedAfterwards()
    {
        var visit = InProgressVisit();
        visit.Report(VisitCause.GridIssue, "a", "b", VisitStart, VisitStart.AddHours(1), UserId, VisitStart.AddHours(2));

        Assert.Throws<DomainException>(() =>
            visit.Report(VisitCause.Other, "c", "d", VisitStart, VisitStart.AddHours(1), UserId, VisitStart.AddHours(3)));
        Assert.Equal(VisitCause.GridIssue, visit.Cause);
    }

    [Fact]
    public void Report_OfPlannedVisit_Throws409() =>
        Assert.Throws<DomainException>(() =>
            NewVisit().Report(VisitCause.Other, "a", "b", VisitStart, VisitStart.AddHours(1), UserId, VisitStart.AddHours(2)));

    [Fact]
    public void Report_RejectsDepartureBeforeArrival_AndFutureTimes()
    {
        var visit = InProgressVisit();

        Assert.Throws<RequestValidationException>(() =>
            visit.Report(VisitCause.Other, "a", "b", VisitStart.AddHours(1), VisitStart, UserId, VisitStart.AddHours(2)));
        Assert.Throws<RequestValidationException>(() =>
            visit.Report(VisitCause.Other, "a", "b", VisitStart, VisitStart.AddHours(5), UserId, VisitStart.AddHours(2)));
        Assert.Equal(ServiceVisitState.InProgress, visit.State);
    }

    [Theory]
    [InlineData("", "work")]
    [InlineData("finding", " ")]
    public void Report_RequiresFindingsAndWork(string findings, string work)
    {
        var visit = InProgressVisit();

        Assert.Throws<RequestValidationException>(() =>
            visit.Report(VisitCause.Other, findings, work, VisitStart, VisitStart.AddHours(1), UserId, VisitStart.AddHours(2)));
    }

    [Fact]
    public void Material_TrimsName_AndRejectsNonPositiveQuantity()
    {
        var material = ServiceVisitMaterial.Create(TicketId, Guid.CreateVersion7(), " Poistka 16A ", 2m, "ks");

        Assert.Equal("Poistka 16A", material.Name);
        Assert.Throws<RequestValidationException>(() => ServiceVisitMaterial.Create(TicketId, Guid.CreateVersion7(), "x", 0m, "ks"));
        Assert.Throws<RequestValidationException>(() => ServiceVisitMaterial.Create(TicketId, Guid.CreateVersion7(), " ", 1m, "ks"));
    }

    [Fact]
    public void Assignment_ReassignChangesTechnician()
    {
        var assignment = ServiceTicketAssignment.Create(TicketId, TechnicianId, UserId, Now);
        var other = Guid.CreateVersion7();

        assignment.Reassign(other, UserId, Now.AddHours(1));

        Assert.Equal(other, assignment.TechnicianId);
        Assert.Equal(Now.AddHours(1), assignment.AssignedAt);
    }

    [Fact]
    public void Ticket_VisitMethods_DriveTheVisitStates_AndWriteHistory()
    {
        var ticket = Remote();

        ticket.ScheduleVisit(UserId, Now);
        ticket.StartVisit(UserId, Now);
        ticket.Verify(UserId, Now);

        Assert.Equal(ServiceTicketState.Verified, ticket.State);
        Assert.Equal(
            [ServiceTicketState.New, ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.VisitScheduled,
             ServiceTicketState.OnSite, ServiceTicketState.Verified],
            ticket.StateChanges.Select(c => c.ToState));
    }

    [Fact]
    public void Ticket_ScheduleVisit_FromNew_Throws409() =>
        Assert.Throws<DomainException>(() => TicketIn().ScheduleVisit(UserId, Now));

    [Fact]
    public void Ticket_Close_FromVerified_WalksResolvedToClosed()
    {
        var ticket = Remote();
        ticket.ScheduleVisit(UserId, Now);
        ticket.StartVisit(UserId, Now);
        ticket.Verify(UserId, Now);

        ticket.Close(UserId, "ok", Now);

        Assert.Equal(ServiceTicketState.Closed, ticket.State);
        Assert.Equal([ServiceTicketState.Resolved, ServiceTicketState.Closed], ticket.StateChanges.TakeLast(2).Select(c => c.ToState));
    }

    [Fact]
    public void Ticket_Close_FromNew_Throws409_AndChangesNothing()
    {
        var ticket = TicketIn();

        Assert.Throws<DomainException>(() => ticket.Close(UserId, null, Now));
        Assert.Equal(ServiceTicketState.New, ticket.State);
        Assert.Single(ticket.StateChanges);
    }

    [Fact]
    public void Ticket_Reopen_WithinSevenDays_ReturnsToRemoteDiagnosis()
    {
        var ticket = Remote();
        ticket.Close(UserId, null, Now);

        ticket.Reopen(UserId, "znova nevyrába", Now.AddDays(7));

        Assert.Equal(ServiceTicketState.RemoteDiagnosis, ticket.State);
        var last = ticket.StateChanges[^1];
        Assert.Equal(ServiceTicketState.Closed, last.FromState);
        Assert.Equal("znova nevyrába", last.Note);
    }

    [Fact]
    public void Ticket_Reopen_AfterSevenDays_Throws409()
    {
        var ticket = Remote();
        ticket.Close(UserId, null, Now);

        Assert.Throws<DomainException>(() => ticket.Reopen(UserId, null, Now.AddDays(7).AddSeconds(1)));
        Assert.Equal(ServiceTicketState.Closed, ticket.State);
    }

    [Fact]
    public void Ticket_Reopen_WhenNotClosed_Throws409() =>
        Assert.Throws<DomainException>(() => Remote().Reopen(UserId, null, Now));

    [Fact]
    public void Ticket_GenericTransitionMap_StillHasNoEdgeOutOfClosed()
    {
        var ticket = Remote();
        ticket.Close(UserId, null, Now);

        Assert.Empty(ticket.AllowedTransitions);
    }
}
