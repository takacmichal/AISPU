using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Tickets;

/// <summary>
/// Service tickets (#35: stories #120 to #123, BPMN #67): <c>/api/tickets</c>. Technician assignment and visits (#36)
/// and the intervention record (#37) will add their own endpoints under the same group.
/// </summary>
public sealed class TicketsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<IAnomalyTicketService, AnomalyTicketService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tickets").WithTags("Tickets");

        group.MapPost("/", CreateAsync)
            .WithName("CreateTicket")
            .WithSummary("Report a fault for an installation (state New)")
            .WithDescription("A customer may report only for its own installation (origin CustomerReport, priority Medium); "
                + "staff create Manual tickets and may set the priority. "
                + $"At most {ServiceTicket.MaxAttachments} attachments (JPEG, PNG or PDF, base64) of {ServiceTicket.MaxAttachmentBytes / (1024 * 1024)} MB.")
            .WithValidation<CreateTicketRequest>()
            .WithMetadata(new RequestSizeLimitAttribute(ServiceTicket.MaxRequestBodyBytes))
            .RequireAuthorization(AuthorizationPolicies.TicketsCreate);

        group.MapGet("/", ListAsync)
            .WithName("ListTickets")
            .WithSummary("List tickets with combinable filters and paging")
            .WithDescription("Filters: state, priority, origin, installationId. Paging: page (default 1), pageSize (default 20, max 100). "
                + "sortBy = createdAt | updatedAt | priority, sortDir = asc | desc (default createdAt desc). A customer sees only its own tickets.")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetTicket")
            .WithSummary("Get a ticket with attachment metadata and allowed transitions")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);

        group.MapPut("/{id:guid}/triage", TriageAsync)
            .WithName("TriageTicket")
            .WithSummary("Set the priority of a new ticket (New to Triaged)")
            .WithValidation<TriageTicketRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);

        group.MapPost("/{id:guid}/transition", TransitionAsync)
            .WithName("TransitionTicket")
            .WithSummary("Move a ticket to another state")
            .WithDescription("Allowed: New→Triaged→RemoteDiagnosis→Resolved→Closed, Verified→Resolved. "
                + "VisitScheduled, OnSite and Verified are set by visits (#36, #37) and give 409 here, as does any edge outside the automaton. "
                + "The user and time of every transition are stored in the history.")
            .WithValidation<TransitionTicketRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.TicketsManage);

        group.MapGet("/{id:guid}/history", HistoryAsync)
            .WithName("GetTicketHistory")
            .WithSummary("State change history, oldest first (read-only)")
            .WithDescription("Customers do not see the internal notes of the transitions.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);

        group.MapPost("/{id:guid}/comments", AddCommentAsync)
            .WithName("AddTicketComment")
            .WithSummary("Add a comment; isPublic = true makes it visible to the customer")
            .WithDescription("Comments cannot be edited or deleted.")
            .WithValidation<AddTicketCommentRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsComment);

        group.MapGet("/{id:guid}/comments", ListCommentsAsync)
            .WithName("ListTicketComments")
            .WithSummary("Comments, oldest first; a customer sees only public ones")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", DownloadAttachmentAsync)
            .WithName("DownloadTicketAttachment")
            .WithSummary("Download one attachment of a ticket")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.TicketsRead);
    }

    internal static async Task<Created<TicketResponse>> CreateAsync(CreateTicketRequest request, ITicketService service, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/tickets/{created.Id}", created);
    }

    internal static async Task<Ok<TicketListResponse>> ListAsync(
        ITicketService service,
        CancellationToken ct,
        ServiceTicketState? state = null,
        ServiceTicketPriority? priority = null,
        ServiceTicketOrigin? origin = null,
        Guid? installationId = null,
        int page = 1,
        int pageSize = 20,
        string? sortBy = null,
        string? sortDir = null) =>
        TypedResults.Ok(await service.ListAsync(
            new TicketListQuery(state, priority, origin, installationId, page, pageSize, sortBy, sortDir), ct));

    internal static async Task<Ok<TicketResponse>> GetAsync(Guid id, ITicketService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<TicketResponse>> TriageAsync(
        Guid id, TriageTicketRequest request, ITicketService service, CancellationToken ct) =>
        TypedResults.Ok(await service.TriageAsync(id, request, ct));

    internal static async Task<Ok<TicketResponse>> TransitionAsync(
        Guid id, TransitionTicketRequest request, ITicketService service, CancellationToken ct) =>
        TypedResults.Ok(await service.TransitionAsync(id, request, ct));

    internal static async Task<Ok<IReadOnlyList<TicketStateChangeResponse>>> HistoryAsync(Guid id, ITicketService service, CancellationToken ct) =>
        TypedResults.Ok(await service.HistoryAsync(id, ct));

    internal static async Task<Created<TicketCommentResponse>> AddCommentAsync(
        Guid id, AddTicketCommentRequest request, ITicketService service, CancellationToken ct)
    {
        var comment = await service.AddCommentAsync(id, request, ct);
        return TypedResults.Created($"/api/tickets/{id}/comments", comment);
    }

    internal static async Task<Ok<IReadOnlyList<TicketCommentResponse>>> ListCommentsAsync(Guid id, ITicketService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListCommentsAsync(id, ct));

    internal static async Task<IResult> DownloadAttachmentAsync(
        Guid id, Guid attachmentId, ITicketService service, HttpContext http, CancellationToken ct)
    {
        var file = await service.DownloadAttachmentAsync(id, attachmentId, ct);
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
}
