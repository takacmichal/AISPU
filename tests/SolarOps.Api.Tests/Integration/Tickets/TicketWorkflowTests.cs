using System.Net;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>States and transitions (#121), comments and history (#123).</summary>
[Collection(PostgresCollection.Name)]
public sealed class TicketWorkflowTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private readonly FixedTimeProvider _clock = new(Start);
    private readonly TicketsFactory _factory;

    public TicketWorkflowTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient As(string role, Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), role);

    [Fact]
    public async Task RemoteResolution_WalksTheAutomaton_AndRecordsUserAndTimeOfEveryStep()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        var operatorId = Guid.CreateVersion7();
        using var op = As(Roles.Operator, operatorId);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        _clock.Advance(TimeSpan.FromMinutes(10));
        using var triage = await op.PutJsonAsync($"/api/tickets/{ticket.Id}/triage",
            new TriageTicketRequest { Priority = ServiceTicketPriority.High, Note = "výpadok výroby" });
        var triaged = await triage.ReadJsonAsync<TicketResponse>();
        _clock.Advance(TimeSpan.FromMinutes(10));
        await op.TransitionAsync(ticket.Id, ServiceTicketState.RemoteDiagnosis);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await op.TransitionAsync(ticket.Id, ServiceTicketState.Resolved, "reštart invertora");
        _clock.Advance(TimeSpan.FromMinutes(10));
        var closed = await op.TransitionAsync(ticket.Id, ServiceTicketState.Closed);

        Assert.Equal(HttpStatusCode.OK, triage.StatusCode);
        Assert.Equal(ServiceTicketPriority.High, triaged.Priority);
        Assert.Equal(ServiceTicketState.Closed, closed.State);
        Assert.Empty(closed.AllowedTransitions);
        Assert.Equal(Start.AddMinutes(40), closed.UpdatedAt);

        var history = await op.GetJsonAsync<List<TicketStateChangeResponse>>($"/api/tickets/{ticket.Id}/history");
        Assert.Equal(
            [ServiceTicketState.New, ServiceTicketState.Triaged, ServiceTicketState.RemoteDiagnosis, ServiceTicketState.Resolved, ServiceTicketState.Closed],
            history.Select(h => h.ToState));
        Assert.Null(history[0].FromState);
        Assert.Equal(ServiceTicketState.RemoteDiagnosis, history[3].FromState);
        Assert.Equal([Start, Start.AddMinutes(10), Start.AddMinutes(20), Start.AddMinutes(30), Start.AddMinutes(40)], history.Select(h => h.ChangedAt));
        Assert.All(history, h => Assert.Equal(operatorId, h.ChangedBy));
        Assert.Equal("výpadok výroby", history[1].Note);
        Assert.Equal("reštart invertora", history[3].Note);
    }

    [Fact]
    public async Task InvalidTransition_Returns409ProblemDetails_AndKeepsState()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        using var skip = await op.PostJsonAsync($"/api/tickets/{ticket.Id}/transition", new TransitionTicketRequest { State = ServiceTicketState.Closed });
        await op.PutJsonAsync($"/api/tickets/{ticket.Id}/triage", new TriageTicketRequest { Priority = ServiceTicketPriority.Low });
        using var triageAgain = await op.PutJsonAsync($"/api/tickets/{ticket.Id}/triage", new TriageTicketRequest { Priority = ServiceTicketPriority.High });

        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        var problem = await skip.ReadJsonAsync<ProblemDetails>();
        Assert.Contains("New", problem.Detail);
        Assert.Equal(HttpStatusCode.Conflict, triageAgain.StatusCode);

        var detail = await op.GetJsonAsync<TicketResponse>($"/api/tickets/{ticket.Id}");
        Assert.Equal(ServiceTicketState.Triaged, detail.State);
        Assert.Equal(ServiceTicketPriority.Low, detail.Priority);
        Assert.Equal(2, (await op.GetJsonAsync<List<TicketStateChangeResponse>>($"/api/tickets/{ticket.Id}/history")).Count);
    }

    [Theory]
    [InlineData(ServiceTicketState.VisitScheduled)]
    [InlineData(ServiceTicketState.OnSite)]
    [InlineData(ServiceTicketState.Verified)]
    public async Task VisitWorkflowStates_AreRefusedByTheGenericTransition(ServiceTicketState target)
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Admin);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        await op.TransitionAsync(ticket.Id, ServiceTicketState.Triaged);
        await op.TransitionAsync(ticket.Id, ServiceTicketState.RemoteDiagnosis);

        using var response = await op.PostJsonAsync($"/api/tickets/{ticket.Id}/transition", new TransitionTicketRequest { State = target });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("#36", (await response.ReadJsonAsync<ProblemDetails>()).Detail);
    }

    [Fact]
    public async Task Transition_WithInvalidBody_Returns400()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        using var noState = await op.PostJsonAsync($"/api/tickets/{ticket.Id}/transition", new { note = "x" });
        using var unknownState = await op.PostJsonAsync($"/api/tickets/{ticket.Id}/transition", new { state = "Teleported" });
        using var noPriority = await op.PutJsonAsync($"/api/tickets/{ticket.Id}/triage", new { note = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, noState.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownState.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noPriority.StatusCode);
    }

    [Fact]
    public async Task Comments_StoreAuthorAndTime_AndCustomerSeesOnlyPublicOnes()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        var technicianId = Guid.CreateVersion7();
        using var technician = As(Roles.Technician, technicianId);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        _clock.Advance(TimeSpan.FromMinutes(5));
        using var internalNote = await technician.PostJsonAsync($"/api/tickets/{ticket.Id}/comments",
            new AddTicketCommentRequest { Body = "Podozrenie na prepätie, objednať diel." });
        _clock.Advance(TimeSpan.FromMinutes(5));
        using var publicNote = await technician.PostJsonAsync($"/api/tickets/{ticket.Id}/comments",
            new AddTicketCommentRequest { Body = "Ozveme sa do 24 hodín.", IsPublic = true });

        Assert.Equal(HttpStatusCode.Created, internalNote.StatusCode);
        var created = await internalNote.ReadJsonAsync<TicketCommentResponse>();
        Assert.Equal(technicianId, created.AuthorId);
        Assert.Equal(Start.AddMinutes(5), created.CreatedAt);
        Assert.False(created.IsPublic);

        var staffView = await op.GetJsonAsync<List<TicketCommentResponse>>($"/api/tickets/{ticket.Id}/comments");
        using var customer = As(Roles.Customer, seed.UserA);
        var customerView = await customer.GetJsonAsync<List<TicketCommentResponse>>($"/api/tickets/{ticket.Id}/comments");

        Assert.Equal(2, staffView.Count);
        var visible = Assert.Single(customerView);
        Assert.Equal("Ozveme sa do 24 hodín.", visible.Body);
        Assert.Equal(Start.AddMinutes(10), visible.CreatedAt);
    }

    [Fact]
    public async Task Comment_WithEmptyBody_Returns400()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        using var response = await op.PostJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddTicketCommentRequest { Body = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_HidesInternalNotesFromTheCustomer()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        await op.TransitionAsync(ticket.Id, ServiceTicketState.Triaged, "interné: zákazník volal trikrát");
        using var customer = As(Roles.Customer, seed.UserA);

        var history = await customer.GetJsonAsync<List<TicketStateChangeResponse>>($"/api/tickets/{ticket.Id}/history");

        Assert.Equal([ServiceTicketState.New, ServiceTicketState.Triaged], history.Select(h => h.ToState));
        Assert.All(history, h => Assert.Null(h.Note));
    }

    [Fact]
    public async Task HistoryAndComments_CannotBeEditedOrDeleted()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var admin = As(Roles.Admin);
        var ticket = await admin.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        using var commentResponse = await admin.PostJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddTicketCommentRequest { Body = "x" });
        var comment = await commentResponse.ReadJsonAsync<TicketCommentResponse>();

        using var deleteHistory = await admin.DeleteAsync($"/api/tickets/{ticket.Id}/history");
        using var editComments = await admin.PutJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "y" });
        using var deleteComment = await admin.DeleteAsync($"/api/tickets/{ticket.Id}/comments/{comment.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteHistory.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, editComments.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteComment.StatusCode);
    }
}
