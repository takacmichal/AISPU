using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>Requests to connect a PV plant to the distribution grid (#85, ADR #217): <c>/api/connection-requests</c>.</summary>
public sealed class ConnectionRequestsModule : IModule
{
    /// <summary>Preparing the documents (BPMN #64, lane Obchod/Operátor).</summary>
    internal static readonly string[] WriteRoles = [Roles.Admin, Roles.Operator, Roles.Sales];

    /// <summary>Back office and the designer (lane Projektant); a customer sees only its own request (see <c>ICustomerScope</c>).</summary>
    internal static readonly string[] ReadRoles = [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Customer];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ConnectionRequestOptions>().Bind(configuration.GetSection(ConnectionRequestOptions.SectionName));
        services.AddScoped<IConnectionRequestService, ConnectionRequestService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connection-requests").WithTags("ConnectionRequests");

        group.MapPost("/", CreateAsync)
            .WithName("CreateConnectionRequest")
            .WithSummary("Create a connection request for an installation (state Draft)")
            .WithDescription("Requires the single-line diagram and the owner's consent as attachments (metadata only, file storage is not defined yet). "
                + "Type is SmallSource up to 10.8 kW rated AC power, otherwise LargeSource.")
            .WithValidation<CreateConnectionRequestRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetConnectionRequest")
            .WithSummary("Get a connection request with its attachments")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));
    }

    internal static async Task<Created<ConnectionRequestResponse>> CreateAsync(
        CreateConnectionRequestRequest request, IConnectionRequestService service, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/connection-requests/{created.Id}", created);
    }

    internal static async Task<Ok<ConnectionRequestResponse>> GetAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));
}
