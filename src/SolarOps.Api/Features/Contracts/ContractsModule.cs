using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Contracts;

/// <summary>Contracts made from accepted quotes (#79) with signature and deposit (#81): <c>/api/contracts</c>.</summary>
public sealed class ContractsModule : IModule
{
    internal static readonly string[] ReadRoles = [Roles.Admin, Roles.Sales, Roles.Designer, Roles.Operator];

    internal static readonly string[] WriteRoles = [Roles.Admin, Roles.Sales];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Community license (ADR spike #219: school project, company under the revenue limit).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddSingleton<IContractPdfGenerator, QuestPdfContractGenerator>();
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

        group.MapPost("/{id:guid}/sign", SignAsync)
            .WithName("SignContract")
            .WithSummary("Record the signature (Draft to Signed) and create the installation")
            .WithDescription("Needs the supply point of the installation. The installation starts in state Planned; "
                + "it cannot move to ComponentsOrdered until the deposit is paid. Only a draft contract can be signed (409 otherwise).")
            .WithValidation<SignContractRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapPost("/{id:guid}/deposit/pay", RecordDepositAsync)
            .WithName("RecordContractDeposit")
            .WithSummary("Record the paid deposit of a signed contract")
            .WithDescription("Only a signed contract; the deposit can be recorded once (409 otherwise). Amount is in EUR, at most the contract total.")
            .WithValidation<RecordDepositRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(p => p.RequireRole(WriteRoles));

        group.MapGet("/{id:guid}/audit", AuditAsync)
            .WithName("GetContractAudit")
            .WithSummary("Audit trail of the contract (created, signed, deposit paid), oldest first")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(p => p.RequireRole(ReadRoles));

        group.MapPost("/{id:guid}/pdf", GeneratePdfAsync)
            .WithName("GenerateContractPdf")
            .WithSummary("Generate the contract as a PDF (application/pdf)")
            .WithDescription("Parties, items and totals of the contract; Slovak, A4. Generated from the current data on each call, nothing is stored.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
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

    internal static async Task<FileContentHttpResult> GeneratePdfAsync(Guid id, IContractService service, CancellationToken ct)
    {
        var pdf = await service.GeneratePdfAsync(id, ct);
        return TypedResults.File(pdf.Content, "application/pdf", pdf.FileName);
    }

    internal static async Task<Ok<ContractResponse>> GetAsync(Guid id, IContractService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    internal static async Task<Ok<IReadOnlyList<ContractResponse>>> ListAsync(
        IContractService service, CancellationToken ct, Guid? customerId = null, Guid? quoteId = null, ContractStatus? status = null) =>
        TypedResults.Ok(await service.ListAsync(customerId, quoteId, status, ct));

    internal static async Task<Ok<ContractResponse>> SignAsync(Guid id, SignContractRequest request, IContractService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SignAsync(id, request, ct));

    internal static async Task<Ok<ContractResponse>> RecordDepositAsync(Guid id, RecordDepositRequest request, IContractService service, CancellationToken ct) =>
        TypedResults.Ok(await service.RecordDepositAsync(id, request, ct));

    internal static async Task<Ok<IReadOnlyList<ContractAuditEntryResponse>>> AuditAsync(Guid id, IContractService service, CancellationToken ct) =>
        TypedResults.Ok(await service.AuditAsync(id, ct));
}
