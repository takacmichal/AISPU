using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Customers;

/// <summary>Customers (#70): <c>/api/customers</c>.</summary>
public sealed class CustomersModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerScope, CustomerScope>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/customers").WithTags("Customers");

        group.MapPost("/", CreateAsync)
            .WithName("CreateCustomer")
            .WithSummary("Create a customer (person or company)")
            .WithValidation<CreateCustomerRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CustomersWrite);

        group.MapGet("/", ListAsync)
            .WithName("ListCustomers")
            .WithSummary("List customers")
            .WithDescription("Filter q matches name, company, e-mail, phone and IČO (case-insensitive substring). Ordered by surname / company name by default; sortBy = name | email | createdAt, sortDir = asc | desc. Defaults: page 1, pageSize 20 (max 100).")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthorizationPolicies.CustomersRead);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetCustomer")
            .WithSummary("Get a customer by id")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CustomersRead);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateCustomer")
            .WithSummary("Update a customer")
            .WithValidation<UpdateCustomerRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CustomersWrite);

        group.MapPost("/{id:guid}/archive", ArchiveAsync)
            .WithName("ArchiveCustomer")
            .WithSummary("Archive a customer")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CustomersWrite);

        group.MapPost("/{id:guid}/reactivate", ReactivateAsync)
            .WithName("ReactivateCustomer")
            .WithSummary("Reactivate an archived customer")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CustomersWrite);
    }

    internal static async Task<Created<CustomerResponse>> CreateAsync(CreateCustomerRequest request, ICustomerService service, CancellationToken ct)
    {
        var customer = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/customers/{customer.Id}", customer);
    }

    internal static async Task<Ok<CustomerListResponse>> ListAsync(
        ICustomerService service, CancellationToken ct, string? q = null, int page = 1, int pageSize = 20, string? sortBy = null, string? sortDir = null) =>
        TypedResults.Ok(await service.ListAsync(q, page, pageSize, sortBy, sortDir, ct));

    internal static async Task<Ok<CustomerResponse>> GetAsync(Guid id, ICustomerService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<CustomerResponse>> UpdateAsync(Guid id, UpdateCustomerRequest request, ICustomerService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, ct));

    internal static async Task<Ok<CustomerResponse>> ArchiveAsync(Guid id, ICustomerService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ArchiveAsync(id, ct));

    internal static async Task<Ok<CustomerResponse>> ReactivateAsync(Guid id, ICustomerService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ReactivateAsync(id, ct));
}
