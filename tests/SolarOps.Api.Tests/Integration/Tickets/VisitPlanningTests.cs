using System.Net;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using static SolarOps.Api.Tests.Integration.Tickets.VisitTestApi;
using InstallationData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>Assigning a technician (#124), planning and rescheduling visits (#125), the technician calendar (#126).</summary>
[Collection(PostgresCollection.Name)]
public sealed class VisitPlanningTests : IDisposable
{
    private readonly FixedTimeProvider _clock = new(Clock);
    private readonly TicketsFactory _factory;

    public VisitPlanningTests(PostgresFixture postgres) => _factory = new TicketsFactory(postgres, _clock);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient As(string role, Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), role);

    [Fact]
    public async Task Assign_Technician_ReturnsAssignment_AndAllowsReassignment()
    {
        var scenario = await ScenarioAsync(_factory);
        var operatorId = Guid.CreateVersion7();
        var other = await InstallationData.SeedTechnicianAsync(_factory);
        using var op = As(Roles.Operator, operatorId);

        var first = await op.PutJsonAsync($"/api/tickets/{scenario.Ticket.Id}/technician", new AssignTechnicianRequest { TechnicianId = scenario.TechnicianId })
            .OkAsync<TicketAssignmentResponse>();
        var second = await op.PutJsonAsync($"/api/tickets/{scenario.Ticket.Id}/technician", new AssignTechnicianRequest { TechnicianId = other })
            .OkAsync<TicketAssignmentResponse>();

        Assert.Equal(scenario.TechnicianId, first.TechnicianId);
        Assert.Equal(operatorId, first.AssignedBy);
        Assert.Equal(other, second.TechnicianId);
        var visits = await op.GetJsonAsync<TicketVisitsResponse>($"/api/tickets/{scenario.Ticket.Id}/visits");
        Assert.Equal(other, visits.AssignedTechnicianId);
        Assert.Empty(visits.Visits);
    }

    [Fact]
    public async Task Assign_UserWithoutTechnicianRole_Returns400()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);

        using var response = await op.PutJsonAsync($"/api/tickets/{scenario.Ticket.Id}/technician", new AssignTechnicianRequest { TechnicianId = scenario.Seed.UserA });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assign_ToNewTicket_Returns409()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        var technician = await InstallationData.SeedTechnicianAsync(_factory);
        using var op = As(Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));

        using var response = await op.PutJsonAsync($"/api/tickets/{ticket.Id}/technician", new AssignTechnicianRequest { TechnicianId = technician });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Assign_UnknownTicket_Returns404_AndEmptyIdReturns400()
    {
        var technician = await InstallationData.SeedTechnicianAsync(_factory);
        using var op = As(Roles.Operator);

        using var missing = await op.PutJsonAsync($"/api/tickets/{Guid.NewGuid()}/technician", new AssignTechnicianRequest { TechnicianId = technician });
        using var empty = await op.PutJsonAsync($"/api/tickets/{Guid.NewGuid()}/technician", new AssignTechnicianRequest { TechnicianId = Guid.Empty });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Assign_WhileVisitIsActive_ToAnotherTechnician_Returns409()
    {
        var scenario = await ScenarioAsync(_factory);
        var other = await InstallationData.SeedTechnicianAsync(_factory);
        using var op = As(Roles.Operator);
        await op.PlannedAsync(scenario);

        using var response = await op.PutJsonAsync($"/api/tickets/{scenario.Ticket.Id}/technician", new AssignTechnicianRequest { TechnicianId = other });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Plan_CreatesVisit_MovesTicketToVisitScheduled_AndWritesHistory()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.AssignAsync(scenario.Ticket.Id, scenario.TechnicianId);

        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/visits", Plan(Day(9), 90));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.ReadJsonAsync<VisitResponse>();
        Assert.Equal($"/api/visits/{visit.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(ServiceVisitState.Planned, visit.State);
        Assert.Equal(Day(9), visit.PlannedStart);
        Assert.Equal(Day(10).AddMinutes(30), visit.PlannedEnd);
        Assert.Equal(90, visit.EstimatedMinutes);
        Assert.Equal(scenario.TechnicianId, visit.TechnicianId);
        var ticket = await op.GetJsonAsync<TicketResponse>($"/api/tickets/{scenario.Ticket.Id}");
        Assert.Equal(ServiceTicketState.VisitScheduled, ticket.State);
        var history = await op.GetJsonAsync<List<TicketStateChangeResponse>>($"/api/tickets/{scenario.Ticket.Id}/history");
        Assert.Equal(ServiceTicketState.VisitScheduled, history[^1].ToState);
    }

    [Fact]
    public async Task Plan_WithoutAssignedTechnician_Returns409_AndKeepsState()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);

        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/visits", Plan(Day(9)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ServiceTicketState.RemoteDiagnosis, (await op.GetJsonAsync<TicketResponse>($"/api/tickets/{scenario.Ticket.Id}")).State);
    }

    [Fact]
    public async Task Plan_SecondVisitForTheSameTicket_Returns409()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.PlannedAsync(scenario);

        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/visits", Plan(Day(14)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(721)]
    public async Task Plan_InvalidDuration_Returns400(int minutes)
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.AssignAsync(scenario.Ticket.Id, scenario.TechnicianId);

        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/visits", Plan(Day(9), minutes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plan_StartInThePast_Returns400()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.AssignAsync(scenario.Ticket.Id, scenario.TechnicianId);

        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/visits", Plan(Clock.AddHours(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plan_NonUtcOffset_IsStoredAsTheSameInstant()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.AssignAsync(scenario.Ticket.Id, scenario.TechnicianId);
        var local = new DateTimeOffset(Day(9).UtcDateTime, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(1));

        var visit = await op.PlanAsync(scenario.Ticket.Id, local);

        Assert.Equal(Day(9), visit.PlannedStart);
    }

    [Fact]
    public async Task Plan_OverlappingVisitOfTheSameTechnician_Returns409_ButAdjacentAndOtherTechnicianPass()
    {
        var first = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.AssignAsync(first.Ticket.Id, first.TechnicianId);
        await op.PlanAsync(first.Ticket.Id, Day(9), 120);

        // Second ticket, same technician: overlap with 09:00 to 11:00.
        var second = await ScenarioAsync(_factory);
        await op.AssignAsync(second.Ticket.Id, first.TechnicianId);
        using var overlap = await op.PostJsonAsync($"/api/tickets/{second.Ticket.Id}/visits", Plan(Day(10), 60));
        var problem = await overlap.ReadJsonAsync<ProblemDetails>();
        var adjacent = await op.PlanAsync(second.Ticket.Id, Day(11), 60);

        // Third ticket, another technician at the same time is fine.
        var third = await ScenarioAsync(_factory);
        await op.AssignAsync(third.Ticket.Id, third.TechnicianId);
        var parallel = await op.PlanAsync(third.Ticket.Id, Day(9), 120);

        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Contains("already has a visit", problem.Detail);
        Assert.Equal(Day(11), adjacent.PlannedStart);
        Assert.Equal(ServiceVisitState.Planned, parallel.State);
        // The failed attempt left the ticket untouched.
        var secondAfter = await op.GetJsonAsync<TicketResponse>($"/api/tickets/{second.Ticket.Id}");
        Assert.Equal(ServiceTicketState.VisitScheduled, secondAfter.State);
    }

    [Fact]
    public async Task Plan_Collision_LeavesTicketInRemoteDiagnosis()
    {
        var first = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        await op.AssignAsync(first.Ticket.Id, first.TechnicianId);
        await op.PlanAsync(first.Ticket.Id, Day(9), 120);
        var second = await ScenarioAsync(_factory);
        await op.AssignAsync(second.Ticket.Id, first.TechnicianId);

        using var overlap = await op.PostJsonAsync($"/api/tickets/{second.Ticket.Id}/visits", Plan(Day(9).AddMinutes(30), 30));

        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        var after = await op.GetJsonAsync<TicketResponse>($"/api/tickets/{second.Ticket.Id}");
        Assert.Equal(ServiceTicketState.RemoteDiagnosis, after.State);
    }

    [Fact]
    public async Task Reschedule_WithReason_ChangesDate_AndKeepsTheLog()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.PlannedAsync(scenario);

        var changed = await op.PutJsonAsync($"/api/visits/{visit.Id}/schedule",
            new RescheduleVisitRequest { Start = Day(13, 2), EstimatedMinutes = 45, Reason = "Zákazník je na dovolenke" }).OkAsync<VisitResponse>();

        Assert.Equal(Day(13, 2), changed.PlannedStart);
        Assert.Equal(Day(13, 2).AddMinutes(45), changed.PlannedEnd);
        var log = Assert.Single(changed.Reschedules);
        Assert.Equal(Day(9), log.PreviousStart);
        Assert.Equal("Zákazník je na dovolenke", log.Reason);
    }

    [Fact]
    public async Task Reschedule_CanOverlapItsOwnOldWindow_ButNotAnotherVisit()
    {
        var first = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.PlannedAsync(first);
        var second = await ScenarioAsync(_factory);
        await op.AssignAsync(second.Ticket.Id, first.TechnicianId);
        await op.PlanAsync(second.Ticket.Id, Day(14), 60);

        var shifted = await op.PutJsonAsync($"/api/visits/{visit.Id}/schedule",
            new RescheduleVisitRequest { Start = Day(9).AddMinutes(30), EstimatedMinutes = 60, Reason = "posun" }).OkAsync<VisitResponse>();
        using var clash = await op.PutJsonAsync($"/api/visits/{visit.Id}/schedule",
            new RescheduleVisitRequest { Start = Day(14).AddMinutes(30), EstimatedMinutes = 60, Reason = "posun" });

        Assert.Equal(Day(9).AddMinutes(30), shifted.PlannedStart);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        var current = await op.GetJsonAsync<VisitResponse>($"/api/visits/{visit.Id}");
        Assert.Equal(Day(9).AddMinutes(30), current.PlannedStart);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Reschedule_WithoutReason_Returns400(string? reason)
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.PlannedAsync(scenario);

        using var response = await op.PutJsonAsync($"/api/visits/{visit.Id}/schedule",
            new RescheduleVisitRequest { Start = Day(13, 2), EstimatedMinutes = 45, Reason = reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_StartedVisit_Returns409_AndUnknownVisitReturns404()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.StartedAsync(scenario);
        var body = new RescheduleVisitRequest { Start = Day(13, 2), EstimatedMinutes = 45, Reason = "x" };

        using var started = await op.PutJsonAsync($"/api/visits/{visit.Id}/schedule", body);
        using var missing = await op.PutJsonAsync($"/api/visits/{Guid.NewGuid()}/schedule", body);

        Assert.Equal(HttpStatusCode.Conflict, started.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Calendar_ReturnsOwnVisitsInPeriod_OrderedByTime_WithAddressAndContact()
    {
        var late = await ScenarioAsync(_factory);
        var technician = late.TechnicianId;
        using var op = As(Roles.Operator);
        await op.AssignAsync(late.Ticket.Id, technician);
        await op.PlanAsync(late.Ticket.Id, Day(14));
        var early = await ScenarioAsync(_factory);
        await op.AssignAsync(early.Ticket.Id, technician);
        await op.PlanAsync(early.Ticket.Id, Day(8));
        var outside = await ScenarioAsync(_factory);
        await op.AssignAsync(outside.Ticket.Id, technician);
        await op.PlanAsync(outside.Ticket.Id, Day(9, 10));
        var foreign = await ScenarioAsync(_factory);
        await op.AssignAsync(foreign.Ticket.Id, foreign.TechnicianId);
        await op.PlanAsync(foreign.Ticket.Id, Day(10));

        using var tech = As(Roles.Technician, technician);
        var calendar = await tech.GetJsonAsync<TechnicianCalendarResponse>(
            $"/api/technicians/me/visits?from={Url(Day(0))}&to={Url(Day(0, 3))}");

        Assert.Equal(technician, calendar.TechnicianId);
        Assert.Equal([early.Ticket.Id, late.Ticket.Id], calendar.Visits.Select(v => v.TicketId));
        var item = calendar.Visits[0];
        Assert.Equal(Day(8), item.Start);
        Assert.Equal(Day(9), item.End);
        Assert.Equal("Invertor hlási chybu", item.TicketTitle);
        Assert.Equal(ServiceTicketPriority.High, item.Priority);
        Assert.Equal(ServiceTicketState.VisitScheduled, item.TicketState);
        Assert.Equal(early.Seed.CustomerA.Id, item.Customer!.Id);
        Assert.Equal(early.Seed.CustomerA.Email, item.Customer.Email);
        Assert.Equal("Hlavná 12", item.Address!.Street);
        Assert.Equal("Nitra", item.Address.City);
    }

    [Fact]
    public async Task Calendar_PlannerCanReadAnyTechnician_AndTechnicianCannot()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.PlannedAsync(scenario);
        var url = $"/api/technicians/{scenario.TechnicianId}/visits?from={Url(Day(0))}&to={Url(Day(0, 2))}";

        var calendar = await op.GetJsonAsync<TechnicianCalendarResponse>(url);
        using var tech = As(Roles.Technician, scenario.TechnicianId);
        using var denied = await tech.GetAsync(url);

        Assert.Equal(visit.Id, Assert.Single(calendar.Visits).VisitId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2026-11-04T00:00:00Z")]
    [InlineData("?from=2026-11-05T00:00:00Z&to=2026-11-04T00:00:00Z")]
    [InlineData("?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z")]
    public async Task Calendar_InvalidRange_Returns400(string query)
    {
        using var tech = As(Roles.Technician);

        using var response = await tech.GetAsync($"/api/technicians/me/visits{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TicketVisits_ForCustomer_HideTechnicianAndFindings_AndForeignTicketIs404()
    {
        var scenario = await ScenarioAsync(_factory);
        using var op = As(Roles.Operator);
        var visit = await op.ReportedAsync(scenario, _clock);

        using var owner = As(Roles.Customer, scenario.Seed.UserA);
        var own = await owner.GetJsonAsync<TicketVisitsResponse>($"/api/tickets/{scenario.Ticket.Id}/visits");
        var ownVisit = await owner.GetJsonAsync<VisitResponse>($"/api/visits/{visit.Id}");
        using var stranger = As(Roles.Customer, scenario.Seed.UserB);
        using var foreignList = await stranger.GetAsync($"/api/tickets/{scenario.Ticket.Id}/visits");
        using var foreignVisit = await stranger.GetAsync($"/api/visits/{visit.Id}");

        Assert.Null(own.AssignedTechnicianId);
        var seen = Assert.Single(own.Visits);
        Assert.Null(seen.TechnicianId);
        Assert.NotNull(seen.Report);
        Assert.Null(seen.Report.Findings);
        Assert.Equal(Day(9), seen.PlannedStart);
        Assert.Equal(visit.Id, ownVisit.Id);
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignVisit.StatusCode);
    }
}
