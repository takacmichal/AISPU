using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.SupplyPoints;

/// <summary>Supply points of a customer (#72): <c>/api/customers/{customerId}/supply-points</c>.</summary>
public sealed class SupplyPointsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISupplyPointService, SupplyPointService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/customers/{customerId:guid}/supply-points").WithTags("SupplyPoints");

        group.MapGet("/", ListAsync)
            .WithName("ListSupplyPoints")
            .WithSummary("List the supply points of a customer")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(CustomersModule.ReadRoles));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetSupplyPoint")
            .WithSummary("Get a supply point")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(CustomersModule.ReadRoles));

        group.MapPost("/", CreateAsync)
            .WithName("CreateSupplyPoint")
            .WithSummary("Add a supply point to a customer")
            .WithDescription("A customer can have several supply points. The EIC code must have a valid format and be unique (409 otherwise).")
            .WithValidation<CreateSupplyPointRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(CustomersModule.WriteRoles));

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSupplyPoint")
            .WithSummary("Update a supply point")
            .WithValidation<UpdateSupplyPointRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(CustomersModule.WriteRoles));

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSupplyPoint")
            .WithSummary("Delete a supply point")
            .WithDescription("Returns 409 when an installation, FVE design or connection request uses the supply point.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(CustomersModule.WriteRoles));
    }

    internal static async Task<Ok<IReadOnlyList<SupplyPointResponse>>> ListAsync(Guid customerId, ISupplyPointService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(customerId, ct));

    internal static async Task<Ok<SupplyPointResponse>> GetAsync(Guid customerId, Guid id, ISupplyPointService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(customerId, id, ct));

    internal static async Task<Created<SupplyPointResponse>> CreateAsync(
        Guid customerId, CreateSupplyPointRequest request, ISupplyPointService service, CancellationToken ct)
    {
        var supplyPoint = await service.CreateAsync(customerId, request, ct);
        return TypedResults.Created($"/api/customers/{customerId}/supply-points/{supplyPoint.Id}", supplyPoint);
    }

    internal static async Task<Ok<SupplyPointResponse>> UpdateAsync(
        Guid customerId, Guid id, UpdateSupplyPointRequest request, ISupplyPointService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(customerId, id, request, ct));

    internal static async Task<NoContent> DeleteAsync(Guid customerId, Guid id, ISupplyPointService service, CancellationToken ct)
    {
        await service.DeleteAsync(customerId, id, ct);
        return TypedResults.NoContent();
    }
}
