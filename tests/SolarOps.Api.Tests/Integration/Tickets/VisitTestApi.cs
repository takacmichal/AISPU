using System.Net;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using InstallationData = SolarOps.Api.Tests.Integration.Installations.InstallationsTestData;

namespace SolarOps.Api.Tests.Integration.Tickets;

/// <summary>Everything a visit test needs: a customer with an installation on a real supply point, a ticket in RemoteDiagnosis and a technician.</summary>
internal sealed record VisitScenario(TicketSeed Seed, Guid TechnicianId, TicketResponse Ticket, Guid SupplyPointId);

internal static class VisitTestApi
{
    public static readonly DateTimeOffset Clock = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A day after the clock at the given hour (UTC).</summary>
    public static DateTimeOffset Day(int hour, int days = 1) => new(Clock.Date.AddDays(days).AddHours(hour), TimeSpan.Zero);

    public static PlanVisitRequest Plan(DateTimeOffset start, int minutes = 60) => new() { Start = start, EstimatedMinutes = minutes };

    public static VisitReportRequest Report(DateTimeOffset arrived, VisitCause cause = VisitCause.InverterFault) => new()
    {
        Cause = cause,
        Findings = "Invertor hlási chybu izolácie DC.",
        WorkDone = "Vymenený DC konektor, reštart invertora.",
        ArrivedAt = arrived,
        DepartedAt = arrived.AddMinutes(75),
    };

    public static string Url(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    /// <summary>Customer A gets a supply point with an address, an installation on it, and a ticket moved to RemoteDiagnosis.</summary>
    public static async Task<VisitScenario> ScenarioAsync(ApiFactory factory, string? role = null)
    {
        var seed = await TicketTestApi.SeedAsync(factory);
        var supplyPointId = Guid.Empty;
        await factory.ExecuteDbAsync(async db =>
        {
            var point = SupplyPoint.Create(
                seed.CustomerA.Id, "Dom", $"24ZZS{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
                new PostalAddress("Hlavná 12", "Nitra", "94901"), 25, Clock);
            db.Add(point);
            await db.SaveChangesAsync();
            supplyPointId = point.Id;
        });

        using var op = factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var installation = (await InstallationData.CreateInstallationAsync(
            op, InstallationData.NewInstallationRequest() with { CustomerId = seed.CustomerA.Id, SupplyPointId = supplyPointId })).Installation;
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(installation.Id));
        using var triage = await op.PutJsonAsync($"/api/tickets/{ticket.Id}/triage", new TriageTicketRequest { Priority = ServiceTicketPriority.High });
        triage.EnsureSuccessStatusCode();
        ticket = await op.TransitionAsync(ticket.Id, ServiceTicketState.RemoteDiagnosis);

        var technicianId = await InstallationData.SeedTechnicianAsync(factory);
        return new VisitScenario(seed, technicianId, ticket, supplyPointId);
    }

    public static async Task<T> OkAsync<T>(this Task<HttpResponseMessage> request)
    {
        using var response = await request;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<T>();
    }

    public static async Task AssignAsync(this HttpClient op, Guid ticketId, Guid technicianId)
    {
        using var response = await op.PutJsonAsync($"/api/tickets/{ticketId}/technician", new AssignTechnicianRequest { TechnicianId = technicianId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<VisitResponse> PlanAsync(this HttpClient op, Guid ticketId, DateTimeOffset start, int minutes = 60)
    {
        using var response = await op.PostJsonAsync($"/api/tickets/{ticketId}/visits", Plan(start, minutes));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<VisitResponse>();
    }

    /// <summary>Assigns the technician and plans a visit tomorrow at 09:00.</summary>
    public static async Task<VisitResponse> PlannedAsync(this HttpClient op, VisitScenario scenario, int hour = 9)
    {
        await op.AssignAsync(scenario.Ticket.Id, scenario.TechnicianId);
        return await op.PlanAsync(scenario.Ticket.Id, Day(hour));
    }

    public static async Task<VisitResponse> StartedAsync(this HttpClient op, VisitScenario scenario)
    {
        var visit = await op.PlannedAsync(scenario);
        return await op.PostAsync($"/api/visits/{visit.Id}/start", null).OkAsync<VisitResponse>();
    }

    /// <summary>Starts the visit, moves the clock past the visit day (a protocol cannot describe the future) and writes the protocol.</summary>
    public static async Task<VisitResponse> ReportedAsync(this HttpClient op, VisitScenario scenario, FixedTimeProvider clock)
    {
        var visit = await op.StartedAsync(scenario);
        clock.Advance(Day(12) - Clock);
        using var response = await op.PostJsonAsync($"/api/visits/{visit.Id}/report", Report(Day(9)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<VisitResponse>();
    }

    /// <summary>Walks the whole visit path and closes the ticket; returns the ticket id state.</summary>
    public static async Task ClosedAsync(this HttpClient op, VisitScenario scenario, FixedTimeProvider clock)
    {
        await op.ReportedAsync(scenario, clock);
        await op.PostAsync($"/api/tickets/{scenario.Ticket.Id}/verify", null).OkAsync<TicketStateResponse>();
        using var response = await op.PostJsonAsync($"/api/tickets/{scenario.Ticket.Id}/close", new CloseTicketRequest());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
