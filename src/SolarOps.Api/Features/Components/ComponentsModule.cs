using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Components;

/// <summary>Catalog of panels, inverters and labor with prices (#73): <c>/api/components</c>.</summary>
public sealed class ComponentsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IComponentService, ComponentService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/components").WithTags("Components");

        group.MapGet("/", ListAsync)
            .WithName("ListComponents")
            .WithSummary("List catalog components")
            .WithDescription("Filters: type, active (true offers only active components, as quotes need), q (code, name, manufacturer). Ordered by type name and component name.")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.CatalogRead);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetComponent")
            .WithSummary("Get a component by id")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CatalogRead);

        group.MapPost("/", CreateAsync)
            .WithName("CreateComponent")
            .WithSummary("Create a component (active)")
            .WithValidation<CreateComponentRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CatalogWrite);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateComponent")
            .WithSummary("Update a component")
            .WithDescription("A price change affects only quotes created afterwards: quotes keep the price they were issued with.")
            .WithValidation<UpdateComponentRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CatalogWrite);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateComponent")
            .WithSummary("Deactivate a component (no longer offered in quotes)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CatalogWrite);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateComponent")
            .WithSummary("Activate a component")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CatalogWrite);
    }

    internal static async Task<Ok<IReadOnlyList<ComponentResponse>>> ListAsync(
        IComponentService service, CancellationToken ct, ComponentType? type = null, bool? active = null, string? q = null) =>
        TypedResults.Ok(await service.ListAsync(type, active, q, ct));

    internal static async Task<Ok<ComponentResponse>> GetAsync(Guid id, IComponentService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Created<ComponentResponse>> CreateAsync(CreateComponentRequest request, IComponentService service, CancellationToken ct)
    {
        var component = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/components/{component.Id}", component);
    }

    internal static async Task<Ok<ComponentResponse>> UpdateAsync(Guid id, UpdateComponentRequest request, IComponentService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, ct));

    internal static async Task<Ok<ComponentResponse>> DeactivateAsync(Guid id, IComponentService service, CancellationToken ct) =>
        TypedResults.Ok(await service.DeactivateAsync(id, ct));

    internal static async Task<Ok<ComponentResponse>> ActivateAsync(Guid id, IComponentService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ActivateAsync(id, ct));
}
