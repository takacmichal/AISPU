using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.Tickets;

public sealed class ServiceTicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid InstallationId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    /// <summary>Allowed edges of the automaton (BPMN #67, mapping #69).</summary>
    public static readonly TheoryData<ServiceTicketState, ServiceTicketState> AllowedEdges = new()
    {
        { ServiceTicketState.New, ServiceTicketState.Triaged },
        { ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis },
        { ServiceTicketState.RemoteDiagnosis, ServiceTicketState.Resolved },
        { ServiceTicketState.RemoteDiagnosis, ServiceTicketState.VisitScheduled },
        { ServiceTicketState.VisitScheduled, ServiceTicketState.OnSite },
        { ServiceTicketState.OnSite, ServiceTicketState.Verified },
        { ServiceTicketState.Verified, ServiceTicketState.Resolved },
        { ServiceTicketState.Resolved, ServiceTicketState.Closed },
    };

    /// <summary>Shortest path from New to every state, so a ticket can be put into any state through the public API.</summary>
    private static readonly Dictionary<ServiceTicketState, ServiceTicketState[]> PathTo = new()
    {
        [ServiceTicketState.New] = [],
        [ServiceTicketState.Triaged] = [ServiceTicketState.Triaged],
        [ServiceTicketState.RemoteDiagnosis] = [ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis],
        [ServiceTicketState.VisitScheduled] = [ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.VisitScheduled],
        [ServiceTicketState.OnSite] = [ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.VisitScheduled, ServiceTicketState.OnSite],
        [ServiceTicketState.Verified] = [ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.VisitScheduled, ServiceTicketState.OnSite, ServiceTicketState.Verified],
        [ServiceTicketState.Resolved] = [ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.Resolved],
        [ServiceTicketState.Closed] = [ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.Resolved, ServiceTicketState.Closed],
    };

    private static ServiceTicket NewTicket(
        ServiceTicketOrigin origin = ServiceTicketOrigin.Manual,
        ServiceTicketPriority priority = ServiceTicketPriority.Medium,
        IReadOnlyCollection<TicketAttachmentInput>? attachments = null) =>
        ServiceTicket.Create(InstallationId, CustomerId, "  Invertor hlási chybu ", " Svieti červená LED. ", priority, origin, attachments ?? [], UserId, Now);

    private static ServiceTicket TicketIn(ServiceTicketState state)
    {
        var ticket = NewTicket();
        foreach (var step in PathTo[state])
        {
            ticket.TransitionTo(step, UserId, null, Now);
        }

        return ticket;
    }

    public static TheoryData<ServiceTicketState, ServiceTicketState> ForbiddenEdges()
    {
        var allowed = AllowedEdges.Select(row => ((ServiceTicketState)row[0], (ServiceTicketState)row[1])).ToHashSet();
        var data = new TheoryData<ServiceTicketState, ServiceTicketState>();
        foreach (var from in Enum.GetValues<ServiceTicketState>())
        {
            foreach (var to in Enum.GetValues<ServiceTicketState>())
            {
                if (!allowed.Contains((from, to)))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    [Fact]
    public void Create_StartsAsNew_TrimsText_AndWritesTheCreationEntry()
    {
        var ticket = NewTicket(ServiceTicketOrigin.CustomerReport, ServiceTicketPriority.High);

        Assert.Equal(ServiceTicketState.New, ticket.State);
        Assert.Equal(ServiceTicketOrigin.CustomerReport, ticket.Origin);
        Assert.Equal(ServiceTicketPriority.High, ticket.Priority);
        Assert.Equal("Invertor hlási chybu", ticket.Title);
        Assert.Equal("Svieti červená LED.", ticket.Description);
        Assert.Equal(InstallationId, ticket.InstallationId);
        Assert.Equal(CustomerId, ticket.CustomerId);
        Assert.Equal(UserId, ticket.CreatedBy);
        Assert.Equal(Now, ticket.CreatedAt);
        Assert.Equal(7, ticket.Id.Version);

        var entry = Assert.Single(ticket.StateChanges);
        Assert.Null(entry.FromState);
        Assert.Equal(ServiceTicketState.New, entry.ToState);
        Assert.Equal(UserId, entry.ChangedBy);
        Assert.Equal(Now, entry.ChangedAt);
    }

    [Fact]
    public void Create_KeepsAttachments()
    {
        var ticket = NewTicket(attachments: [new TicketAttachmentInput("porucha.jpg", "image/jpeg", [1, 2, 3])]);

        var attachment = Assert.Single(ticket.Attachments);
        Assert.Equal(ticket.Id, attachment.ServiceTicketId);
        Assert.Equal("porucha.jpg", attachment.FileName);
        Assert.Equal("image/jpeg", attachment.ContentType);
        Assert.Equal(3, attachment.SizeBytes);
        Assert.Equal(Now, attachment.CreatedAt);
    }

    [Fact]
    public void Create_WithEmptyInstallationOrTitle_Throws()
    {
        Assert.Throws<RequestValidationException>(() =>
            ServiceTicket.Create(Guid.Empty, CustomerId, "T", "D", ServiceTicketPriority.Low, ServiceTicketOrigin.Manual, [], UserId, Now));
        Assert.Throws<RequestValidationException>(() =>
            ServiceTicket.Create(InstallationId, CustomerId, " ", "D", ServiceTicketPriority.Low, ServiceTicketOrigin.Manual, [], UserId, Now));
    }

    [Theory]
    [MemberData(nameof(AllowedEdges))]
    public void TransitionTo_AllowedEdge_ChangesStateAndRecordsWhoAndWhen(ServiceTicketState from, ServiceTicketState to)
    {
        var ticket = TicketIn(from);
        var later = Now.AddHours(2);
        var actor = Guid.CreateVersion7();

        ticket.TransitionTo(to, actor, "  poznámka ", later);

        Assert.Equal(to, ticket.State);
        Assert.Equal(later, ticket.UpdatedAt);
        var entry = ticket.StateChanges[^1];
        Assert.Equal(from, entry.FromState);
        Assert.Equal(to, entry.ToState);
        Assert.Equal(actor, entry.ChangedBy);
        Assert.Equal(later, entry.ChangedAt);
        Assert.Equal("poznámka", entry.Note);
    }

    [Theory]
    [MemberData(nameof(ForbiddenEdges))]
    public void TransitionTo_ForbiddenEdge_ThrowsDomainException_AndChangesNothing(ServiceTicketState from, ServiceTicketState to)
    {
        var ticket = TicketIn(from);
        var historyBefore = ticket.StateChanges.Count;

        Assert.Throws<DomainException>(() => ticket.TransitionTo(to, UserId, null, Now.AddHours(1)));

        Assert.Equal(from, ticket.State);
        Assert.Equal(historyBefore, ticket.StateChanges.Count);
    }

    [Fact]
    public void AllowedTransitions_FollowTheMap()
    {
        Assert.Equal([ServiceTicketState.Triaged], TicketIn(ServiceTicketState.New).AllowedTransitions);
        Assert.Equal(
            [ServiceTicketState.VisitScheduled, ServiceTicketState.Resolved],
            TicketIn(ServiceTicketState.RemoteDiagnosis).AllowedTransitions.Order());
        Assert.Empty(TicketIn(ServiceTicketState.Closed).AllowedTransitions);
    }

    [Fact]
    public void Triage_SetsPriority_AndMovesNewToTriaged()
    {
        var ticket = NewTicket(priority: ServiceTicketPriority.Medium);

        ticket.Triage(ServiceTicketPriority.High, UserId, "kritické", Now.AddMinutes(5));

        Assert.Equal(ServiceTicketState.Triaged, ticket.State);
        Assert.Equal(ServiceTicketPriority.High, ticket.Priority);
        Assert.Equal("kritické", ticket.StateChanges[^1].Note);
    }

    [Fact]
    public void Triage_OutsideNew_ThrowsAndKeepsPriority()
    {
        var ticket = TicketIn(ServiceTicketState.Triaged);

        Assert.Throws<DomainException>(() => ticket.Triage(ServiceTicketPriority.Low, UserId, null, Now));

        Assert.Equal(ServiceTicketPriority.Medium, ticket.Priority);
    }

    [Fact]
    public void VisitWorkflowStates_AreTheStatesOwnedByVisitsAndInterventions()
    {
        Assert.Equal(
            [ServiceTicketState.VisitScheduled, ServiceTicketState.OnSite, ServiceTicketState.Verified],
            ServiceTicket.VisitWorkflowStates.Order());
    }

    [Fact]
    public void Comment_StoresAuthorTimeAndVisibility()
    {
        var ticketId = Guid.CreateVersion7();

        var comment = ServiceTicketComment.Create(ticketId, UserId, "  Volal som zákazníkovi. ", isPublic: true, Now);

        Assert.Equal(ticketId, comment.ServiceTicketId);
        Assert.Equal(UserId, comment.AuthorId);
        Assert.Equal(Now, comment.CreatedAt);
        Assert.Equal("Volal som zákazníkovi.", comment.Body);
        Assert.True(comment.IsPublic);
    }

    [Fact]
    public void Comment_Empty_Throws() =>
        Assert.Throws<RequestValidationException>(() => ServiceTicketComment.Create(Guid.CreateVersion7(), UserId, "  ", false, Now));
}
