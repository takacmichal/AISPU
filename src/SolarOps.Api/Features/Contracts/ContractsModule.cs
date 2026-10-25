using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Contracts made from accepted quotes (#79): <c>/api/contracts</c>.</summary>
public sealed class ContractsModule : IModule
{
    internal static readonly string[] ReadRoles = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Operator];

    internal static readonly string[] WriteRoles = [Roles.Admin, Roles.Sales];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IContractService, ContractService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/contracts").WithTags("Contracts");

        group.MapPost("/", CreateAsync)
            .WithName("CreateContract")
            .WithSummary("Create a contract (state Draft) from an accepted quote")
            .WithDescription("Copies customer, lines and prices from the quote and assigns a unique number. "
                + "A quote that is not Accepted, or that already has a contract, gives 409.")
            .WithValidation<CreateContractRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetContract")
            .WithSummary("Get a contract with its lines and totals")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/", ListAsync)
            .WithName("ListContracts")
            .WithSummary("List contracts")
            .WithDescription("Filters: customerId, quoteId, status. Newest first.")
            .RequireAuthorization(p => p.RequireRole(ReadRoles));
    }

    internal static async Task<Created<ContractResponse>> CreateAsync(CreateContractRequest request, IContractService service, CancellationToken ct)
    {
        var contract = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/contracts/{contract.Id}", contract);
    }

    internal static async Task<Ok<ContractResponse>> GetAsync(Guid id, IContractService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<IReadOnlyList<ContractResponse>>> ListAsync(
        IContractService service, CancellationToken ct, Guid? customerId = null, Guid? quoteId = null, ContractStatus? status = null) =>
        TypedResults.Ok(await service.ListAsync(customerId, quoteId, status, ct));
}
