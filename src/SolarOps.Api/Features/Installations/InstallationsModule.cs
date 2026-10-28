using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
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
            .WithDescription("Filters: state, customerId, contractId, supplyPointId, assignedToMe (installations of the current technician).")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

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
            .WithValidation<ScheduleInstallationRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapPut("/{id:guid}/technicians", AssignTechnicians)
            .WithName("AssignInstallationTechnicians")
            .WithSummary("Replace the assigned technicians (users with role Technician)")
            .WithValidation<AssignTechniciansRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsPlan);

        group.MapPost("/{id:guid}/transitions", Transition)
            .WithName("TransitionInstallation")
            .WithSummary("Change the installation state")
            .WithDescription("Only transitions listed in allowedTransitions are accepted, otherwise 409. Mounting requires a schedule and at least one technician.")
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
        bool assignedToMe = false)
    {
        var query = new InstallationQuery(ParseState(state), customerId, contractId, supplyPointId, assignedToMe);
        return TypedResults.Ok(await service.ListAsync(query, ct));
    }

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
