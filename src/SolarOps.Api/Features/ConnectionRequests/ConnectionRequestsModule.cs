using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.ConnectionRequests;

/// <summary>
/// Connection requests for the distributor (BPMN #64, story #85): <c>/api/connection-requests</c>.
/// A request starts Ready with the mandatory attachments; submitting and deadlines follow in #86/#87.
/// </summary>
public sealed class ConnectionRequestsModule : IModule
{
    internal static readonly string[] ReadRoles =
        [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Technician, Roles.Operator];

    /// <summary>Sales prepares the request (story #85), the designer owns the technical attachments.</summary>
    internal static readonly string[] WriteRoles = [Roles.Admin, Roles.Sales, Roles.Designer];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConnectionRequestOptions>(configuration.GetSection(ConnectionRequestOptions.SectionName));
        services.AddScoped<IConnectionRequestService, ConnectionRequestService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connection-requests").WithTags("ConnectionRequests");

        group.MapPost("/", CreateAsync)
            .WithName("CreateConnectionRequest")
            .WithSummary("Create a connection request (state Ready) for an installation")
            .WithDescription("Copies the customer and supply point from the installation and derives "
                + "the type (SmallSource/LargeSource) from the total inverter AC power. "
                + "The single-line diagram and the owner's consent are mandatory. "
                + "One request per installation; a second one gives 409.")
            .WithValidation<CreateConnectionRequestRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/", ListAsync)
            .WithName("ListConnectionRequests")
            .WithSummary("List connection requests")
            .WithDescription("Filters: installationId, customerId, status. Newest first.")
            .ProducesValidationProblem()
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetConnectionRequest")
            .WithSummary("Get a connection request with attachment metadata (not the file contents)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateConnectionRequest")
            .WithSummary("Replace the power, note and attachments of a Ready request")
            .WithDescription("Only a request in state Ready can be edited; a submitted request gives 409.")
            .WithValidation<UpdateConnectionRequestRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", DownloadAttachmentAsync)
            .WithName("DownloadConnectionRequestAttachment")
            .WithSummary("Download one attachment of a connection request")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));
    }

    internal static async Task<Created<ConnectionRequestResponse>> CreateAsync(
        CreateConnectionRequestRequest request, IConnectionRequestService service, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/connection-requests/{created.Id}", created);
    }

    internal static async Task<Ok<ConnectionRequestResponse>> GetAsync(
        Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<IReadOnlyList<ConnectionRequestResponse>>> ListAsync(
        IConnectionRequestService service,
        CancellationToken ct,
        Guid? installationId = null,
        Guid? customerId = null,
        ConnectionRequestStatus? status = null) =>
        TypedResults.Ok(await service.ListAsync(installationId, customerId, status, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> UpdateAsync(
        Guid id, UpdateConnectionRequestRequest request, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, ct));

    internal static async Task<IResult> DownloadAttachmentAsync(
        Guid id, Guid attachmentId, IConnectionRequestService service, CancellationToken ct)
    {
        var file = await service.DownloadAttachmentAsync(id, attachmentId, ct);
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
}
