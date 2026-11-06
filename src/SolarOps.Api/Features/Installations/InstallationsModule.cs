using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Querying;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Installations;

/// <summary>Installations (#82): planning, scheduling, technicians and the installation lifecycle.</summary>
public sealed class InstallationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInstallationService, InstallationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/installations").WithTags("Installations");

        group.MapGet("/", List)
            .WithName("ListInstallations")
            .WithSummary("List installations")
            .WithDescription("Filters: state, customerId, contractId, supplyPointId, assignedToMe (installations of the current technician), monitoringEnabled (plants in operation, #92).")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapGet("/paged", ListPaged)
            .WithName("ListInstallationsPaged")
            .WithSummary("List installations with paging, search and state counts")
            .WithDescription("Filters: state, customerId, technicianId (assigned technician), q (installation name, supply point name or address, customer name; case-insensitive substring). "
                + "sortBy = createdAt | updatedAt | name | state | scheduledStart | plannedPowerKwp, sortDir = asc | desc (default createdAt desc). Defaults: page 1, pageSize 20 (max 100). "
                + "statusCounts holds the installations per state for the current q, customerId and technicianId (the state filter is ignored). A customer sees only its own installations.")
            .WithValidation<PageQuery>()
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapGet("/technicians", ListTechnicians)
            .WithName("ListInstallationTechnicians")
            .WithSummary("List users with role Technician that can be assigned to installations")
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapGet("/{id:guid}", Get)
            .WithName("GetInstallation")
            .WithSummary("Get an installation with its state history")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapPost("/", Create)
            .WithName("CreateInstallation")
            .WithSummary("Create an installation for a contract and supply point (state Planned)")
            .WithValidation<CreateInstallationRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapPut("/{id:guid}", Update)
            .WithName("UpdateInstallation")
            .WithSummary("Update name, planned power and note")
            .WithValidation<UpdateInstallationRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapPut("/{id:guid}/schedule", Schedule)
            .WithName("ScheduleInstallation")
            .WithSummary("Set the planned mounting window")
            .WithDescription("Requires an approved connection request (409), dates must not be in the past (400) and assigned technicians must be free in the window (409).")
            .WithValidation<ScheduleInstallationRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapPut("/{id:guid}/technicians", AssignTechnicians)
            .WithName("AssignInstallationTechnicians")
            .WithSummary("Replace the assigned technicians (users with role Technician)")
            .WithDescription("A technician already planned on another installation in an overlapping window gives 409.")
            .WithValidation<AssignTechniciansRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapPost("/{id:guid}/transitions", Transition)
            .WithName("TransitionInstallation")
            .WithSummary("Change the installation state")
            .WithDescription("Only transitions listed in allowedTransitions are accepted, otherwise 409. Mounting requires a schedule and at least one technician, "
                + "MountingDone a complete checklist. InspectionPassed/InspectionFailed are set by a revision report and Commissioned by /commission.")
            .WithValidation<InstallationTransitionRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsField);
    }

    internal static async Task<Ok<IReadOnlyList<InstallationResponse>>> List(
        IInstallationService service,
        CancellationToken ct,
        string? state = null,
        Guid? customerId = null,
        Guid? contractId = null,
        Guid? supplyPointId = null,
        bool assignedToMe = false,
        bool? monitoringEnabled = null)
    {
        var query = new InstallationQuery(ParseState(state), customerId, contractId, supplyPointId, assignedToMe, monitoringEnabled);
        return TypedResults.Ok(await service.ListAsync(query, ct));
    }

    internal static async Task<Ok<InstallationPageResponse>> ListPaged(
        [AsParameters] PageQuery page, IInstallationService service, CancellationToken ct,
        string? state = null, Guid? customerId = null, Guid? technicianId = null) =>
        TypedResults.Ok(await service.ListPagedAsync(page, state, customerId, technicianId, ct));

    internal static async Task<Ok<IReadOnlyList<TechnicianResponse>>> ListTechnicians(IInstallationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListTechniciansAsync(ct));

    internal static async Task<Ok<InstallationDetailResponse>> Get(Guid id, IInstallationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Created<InstallationDetailResponse>> Create(
        CreateInstallationRequest request, IInstallationService service, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/installations/{created.Installation.Id}", created);
    }

    internal static async Task<Ok<InstallationDetailResponse>> Update(
        Guid id, UpdateInstallationRequest request, IInstallationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, ct));

    internal static async Task<Ok<InstallationDetailResponse>> Schedule(
        Guid id, ScheduleInstallationRequest request, IInstallationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ScheduleAsync(id, request, ct));

    internal static async Task<Ok<InstallationDetailResponse>> AssignTechnicians(
        Guid id, AssignTechniciansRequest request, IInstallationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.AssignTechniciansAsync(id, request, ct));

    internal static async Task<Ok<InstallationDetailResponse>> Transition(
        Guid id, InstallationTransitionRequest request, IInstallationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.TransitionAsync(id, request, ct));

    private static InstallationState? ParseState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return Enum.TryParse<InstallationState>(state, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new RequestValidationException("state", $"Unknown installation state '{state}'. Allowed: {string.Join(", ", Enum.GetNames<InstallationState>())}.");
    }
}
