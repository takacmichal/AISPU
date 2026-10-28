using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>Submitting to the distributor, reviewing its conditions and submitting the signed contract (lane Projektant).</summary>
    internal static readonly string[] SubmitRoles = [Roles.Admin, Roles.Designer];

    /// <summary>Back-office steps (lane Obchod/Operátor): resubmit, send the contract, record the signature. ADR #207 gives Sales T and Designer RWT.</summary>
    internal static readonly string[] ResubmitRoles = [Roles.Admin, Roles.Operator, Roles.Sales, Roles.Designer];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ConnectionRequestOptions>()
            .Bind(configuration.GetSection(ConnectionRequestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IConnectionRequestService, ConnectionRequestService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connection-requests").WithTags("ConnectionRequests");

        group.MapPost("/", CreateAsync)
            .WithName("CreateConnectionRequest")
            .WithSummary("Create a connection request for an installation (state Draft)")
            .WithDescription("Requires the single-line diagram and the owner's consent as attachments. "
                + "Attachments may carry base64 content (stored and downloadable) or metadata only; "
                + $"at most {ConnectionRequest.MaxAttachments} attachments of {ConnectionRequest.MaxAttachmentBytes / (1024 * 1024)} MB, "
                + $"request body up to {ConnectionRequest.MaxRequestBodyBytes / (1024 * 1024)} MB. "
                + "Type is SmallSource up to 10.8 kW rated AC power, otherwise LargeSource.")
            .WithValidation<CreateConnectionRequestRequest>()
            // Without this the Kestrel default (~30 MB) would reject bodies that the per-attachment limits allow.
            .WithMetadata(new RequestSizeLimitAttribute(ConnectionRequest.MaxRequestBodyBytes))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/", ListAsync)
            .WithName("ListConnectionRequests")
            .WithSummary("List connection requests")
            .WithDescription("Filters: installationId, customerId, state, overdue (true: submitted requests past the proposal deadline). "
                + "Newest first. A customer sees only its own requests.")
            .ProducesValidationProblem()
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetConnectionRequest")
            .WithSummary("Get a connection request with its attachments")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateConnectionRequest")
            .WithSummary("Replace the EIC, power, breaker and attachments of a Draft or InfoRequested request")
            .WithDescription("Only a request in state Draft or InfoRequested can be edited; anything else gives 409. The installation stays the same.")
            .WithValidation<UpdateConnectionRequestRequest>()
            .WithMetadata(new RequestSizeLimitAttribute(ConnectionRequest.MaxRequestBodyBytes))
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapPost("/{id:guid}/submit", SubmitAsync)
            .WithName("SubmitConnectionRequest")
            .WithSummary("Submit the request to the distributor (Draft to Submitted)")
            .WithDescription("Starts the proposal deadline: 10 working days for a small source, 30 calendar days for a larger one (configurable, "
                + "section ConnectionRequest). The response carries proposalDueDate, proposalRemainingDays and proposalOverdue.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(SubmitRoles));

        group.MapPost("/{id:guid}/resubmit", ResubmitAsync)
            .WithName("ResubmitConnectionRequest")
            .WithSummary("Send the completed request again (InfoRequested to Submitted)")
            .WithDescription("Restarts the proposal deadline. Correct the request first with PUT.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(ResubmitRoles));

        group.MapPost("/{id:guid}/review-conditions", ReviewConditionsAsync)
            .WithName("ReviewConnectionRequestConditions")
            .WithSummary("Record that the technical conditions of the offered contract were checked (ContractOffered)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(SubmitRoles));

        group.MapPost("/{id:guid}/send-contract", SendContractAsync)
            .WithName("SendConnectionContract")
            .WithSummary("Record that the contract was sent to the customer (ContractOffered)")
            .WithDescription("The conditions must be reviewed first, otherwise 409. Sending the notification is not part of this story (#130).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(ResubmitRoles));

        group.MapPost("/{id:guid}/customer-signed", CustomerSignedAsync)
            .WithName("MarkConnectionContractSigned")
            .WithSummary("Record the customer's signature (ContractOffered to ContractSigned)")
            .WithDescription("The contract must have been sent to the customer, otherwise 409.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(ResubmitRoles));

        group.MapPost("/{id:guid}/submit-signed-contract", SubmitSignedContractAsync)
            .WithName("SubmitSignedConnectionContract")
            .WithSummary("Send the signed contract to the distributor (ContractSigned to ContractSubmitted)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(SubmitRoles));

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", DownloadAttachmentAsync)
            .WithName("DownloadConnectionRequestAttachment")
            .WithSummary("Download the content of one attachment")
            .WithDescription("Only attachments sent with base64 content can be downloaded; metadata-only attachments give 404.")
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

    internal static async Task<Ok<IReadOnlyList<ConnectionRequestResponse>>> ListAsync(
        IConnectionRequestService service,
        CancellationToken ct,
        Guid? installationId = null,
        Guid? customerId = null,
        ConnectionRequestState? state = null,
        bool? overdue = null) =>
        TypedResults.Ok(await service.ListAsync(installationId, customerId, state, overdue, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> SubmitAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SubmitAsync(id, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> ResubmitAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ResubmitAsync(id, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> UpdateAsync(
        Guid id, UpdateConnectionRequestRequest request, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> ReviewConditionsAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ReviewConditionsAsync(id, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> SendContractAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SendContractAsync(id, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> CustomerSignedAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.CustomerSignedAsync(id, ct));

    internal static async Task<Ok<ConnectionRequestResponse>> SubmitSignedContractAsync(Guid id, IConnectionRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SubmitSignedContractAsync(id, ct));

    internal static async Task<IResult> DownloadAttachmentAsync(
        Guid id, Guid attachmentId, IConnectionRequestService service, HttpContext http, CancellationToken ct)
    {
        var file = await service.DownloadAttachmentAsync(id, attachmentId, ct);
        // The content type comes from the client; never let the browser sniff something else into executing it.
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
}
