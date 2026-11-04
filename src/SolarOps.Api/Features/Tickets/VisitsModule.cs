using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Tickets;

/// <summary>
/// Technician assignment and visits (#36: stories #124 to #126) and the intervention record with material, device
/// replacement and closing (#37: stories #127 to #129). Extends the ticket automaton of <see cref="TicketsModule"/>
/// with the states VisitScheduled, OnSite and Verified.
/// </summary>
public sealed class VisitsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IVisitService, VisitService>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var tickets = endpoints.MapGroup("/api/tickets/{id:guid}").WithTags("Visits");

        tickets.MapPut("/technician", AssignTechnicianAsync)
            .WithName("AssignTicketTechnician")
            .WithSummary("Assign a technician to the ticket (user with role Technician)")
            .WithDescription("Possible in states Triaged, RemoteDiagnosis and VisitScheduled; the technician cannot change while a visit is active.")
            .WithValidation<AssignTechnicianRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);

        tickets.MapPost("/visits", PlanAsync)
            .WithName("PlanTicketVisit")
            .WithSummary("Plan a visit of the assigned technician (RemoteDiagnosis to VisitScheduled)")
            .WithDescription("Start and estimated duration (15 to 720 minutes). 409 when no technician is assigned, the ticket is in another state "
                + "or the technician has an overlapping Planned or InProgress visit.")
            .WithValidation<PlanVisitRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);

        tickets.MapGet("/visits", ListForTicketAsync)
            .WithName("ListTicketVisits")
            .WithSummary("Visits of a ticket with the assigned technician")
            .WithDescription("A customer sees only its own tickets, without the technician and the internal findings.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);

        tickets.MapPost("/verify", VerifyAsync)
            .WithName("VerifyTicketProduction")
            .WithSummary("The technician verifies that production is restored (OnSite to Verified)")
            .WithDescription("Requires a written service protocol. A technician may verify only own tickets.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsField);

        tickets.MapPost("/close", CloseAsync)
            .WithName("CloseTicket")
            .WithSummary("Resolve and close the ticket (Verified or RemoteDiagnosis to Resolved to Closed)")
            .WithDescription("A ticket with a visit needs its service protocol, otherwise 409.")
            .WithValidation<CloseTicketRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);

        tickets.MapPost("/confirm", ConfirmAsync)
            .WithName("ConfirmTicket")
            .WithSummary("The customer confirms that the fault is fixed (closed ticket)")
            .WithValidation<ConfirmTicketRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsConfirm);

        tickets.MapPost("/reopen", ReopenAsync)
            .WithName("ReopenTicket")
            .WithSummary("The customer reopens a closed ticket within 7 days (back to RemoteDiagnosis)")
            .WithValidation<ReopenTicketRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsConfirm);

        var visits = endpoints.MapGroup("/api/visits/{id:guid}").WithTags("Visits");

        visits.MapGet("/", GetAsync)
            .WithName("GetVisit")
            .WithSummary("Get a visit with protocol, reschedule log, material and replaced devices")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);

        visits.MapPut("/schedule", RescheduleAsync)
            .WithName("RescheduleVisit")
            .WithSummary("Change the date of a planned visit with a reason")
            .WithValidation<RescheduleVisitRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);

        visits.MapPost("/start", StartAsync)
            .WithName("StartVisit")
            .WithSummary("The technician arrived (visit InProgress, ticket OnSite)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsField);

        visits.MapPost("/report", ReportAsync)
            .WithName("ReportVisit")
            .WithSummary("Write the service protocol: cause, findings, work done, arrival and departure (visit Done)")
            .WithDescription("Written once; the protocol cannot be changed afterwards (409).")
            .WithValidation<VisitReportRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsField);

        visits.MapPut("/materials", ReplaceMaterialsAsync)
            .WithName("ReplaceVisitMaterials")
            .WithSummary("Replace the list of used material of the visit")
            .WithDescription("Possible while the technician is on site, before the ticket is verified.")
            .WithValidation<ReplaceVisitMaterialsRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsField);

        visits.MapPost("/device-replacements", ReplaceDeviceAsync)
            .WithName("ReplaceVisitDevice")
            .WithSummary("Replace a panel or inverter of the installation during the visit")
            .WithDescription("Uses the device replacement of the installation (old device stays in history, serial number validated and unique) and links it to the visit.")
            .WithValidation<VisitDeviceReplacementRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsField);

        var technicians = endpoints.MapGroup("/api/technicians").WithTags("Visits");

        technicians.MapGet("/me/visits", MyCalendarAsync)
            .WithName("GetMyVisits")
            .WithSummary("Visit calendar of the current technician for a period")
            .WithDescription("Query from and to (ISO 8601, at most 92 days); visits starting in [from, to) ordered by time, with address and customer contact.")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.TicketsField);

        technicians.MapGet("/{technicianId:guid}/visits", CalendarAsync)
            .WithName("GetTechnicianVisits")
            .WithSummary("Visit calendar of any technician for a period (planners)")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);
    }

    internal static async Task<Ok<TicketAssignmentResponse>> AssignTechnicianAsync(
        Guid id, AssignTechnicianRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.AssignTechnicianAsync(id, request, ct));

    internal static async Task<Created<VisitResponse>> PlanAsync(Guid id, PlanVisitRequest request, IVisitService service, CancellationToken ct)
    {
        var visit = await service.PlanAsync(id, request, ct);
        return TypedResults.Created($"/api/visits/{visit.Id}", visit);
    }

    internal static async Task<Ok<TicketVisitsResponse>> ListForTicketAsync(Guid id, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListForTicketAsync(id, ct));

    internal static async Task<Ok<TicketStateResponse>> VerifyAsync(Guid id, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.VerifyAsync(id, ct));

    internal static async Task<Ok<TicketStateResponse>> CloseAsync(Guid id, CloseTicketRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.CloseAsync(id, request, ct));

    internal static async Task<Ok<TicketConfirmationResponse>> ConfirmAsync(
        Guid id, ConfirmTicketRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ConfirmAsync(id, request, ct));

    internal static async Task<Ok<TicketConfirmationResponse>> ReopenAsync(
        Guid id, ReopenTicketRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ReopenAsync(id, request, ct));

    internal static async Task<Ok<VisitResponse>> GetAsync(Guid id, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<VisitResponse>> RescheduleAsync(
        Guid id, RescheduleVisitRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.RescheduleAsync(id, request, ct));

    internal static async Task<Ok<VisitResponse>> StartAsync(Guid id, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.StartAsync(id, ct));

    internal static async Task<Ok<VisitResponse>> ReportAsync(Guid id, VisitReportRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ReportAsync(id, request, ct));

    internal static async Task<Ok<VisitResponse>> ReplaceMaterialsAsync(
        Guid id, ReplaceVisitMaterialsRequest request, IVisitService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ReplaceMaterialsAsync(id, request, ct));

    internal static async Task<Created<VisitDeviceReplacementResult>> ReplaceDeviceAsync(
        Guid id, VisitDeviceReplacementRequest request, IVisitService service, CancellationToken ct)
    {
        var result = await service.ReplaceDeviceAsync(id, request, ct);
        return TypedResults.Created($"/api/visits/{id}", result);
    }

    internal static async Task<Ok<TechnicianCalendarResponse>> MyCalendarAsync(
        IVisitService service, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        TypedResults.Ok(await service.MyCalendarAsync(from, to, ct));

    internal static async Task<Ok<TechnicianCalendarResponse>> CalendarAsync(
        Guid technicianId, IVisitService service, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        TypedResults.Ok(await service.CalendarAsync(technicianId, from, to, ct));
}
