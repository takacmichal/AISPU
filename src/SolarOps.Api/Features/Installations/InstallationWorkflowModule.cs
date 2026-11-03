using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Installations;

/// <summary>
/// Realization and hand-over of an installation (#24, #25): assembly checklist (#90), revision report (#91),
/// commissioning (#92) and the documentation package for the customer (#93).
/// </summary>
public sealed class InstallationWorkflowModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddSingleton<IHandoverProtocolPdfGenerator, QuestPdfHandoverProtocolGenerator>();
        services.AddScoped<IInstallationWorkflowService, InstallationWorkflowService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/installations/{id:guid}").WithTags("Installations");

        group.MapGet("/checklist", GetChecklist)
            .WithName("GetInstallationChecklist")
            .WithSummary("Assembly checklist: template steps with their state")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapPut("/checklist/{stepCode}", UpdateChecklistStep)
            .WithName("UpdateInstallationChecklistStep")
            .WithSummary("Mark a checklist step done or not done, with an optional note")
            .WithDescription("Only while the installation is Mounting, otherwise 409. Unknown step code gives 404. Each change stores who and when.")
            .WithValidation<UpdateChecklistStepRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsField);

        group.MapGet("/revision-reports", ListRevisionReports)
            .WithName("ListInstallationRevisionReports")
            .WithSummary("Revision reports of an installation (metadata, oldest first)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapGet("/revision-reports/{reportId:guid}", GetRevisionReport)
            .WithName("GetInstallationRevisionReport")
            .WithSummary("Revision report metadata")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapPost("/revision-reports", RecordRevisionReport)
            .WithName("RecordInstallationRevisionReport")
            .WithSummary("Record the revision report with its file (state Inspection)")
            .WithDescription($"Passed moves the installation to InspectionPassed, Failed to InspectionFailed (repairs, then rework to Mounting). "
                + $"File as base64: PDF, PNG or JPEG up to {RevisionReport.MaxFileBytes / (1024 * 1024)} MB, the content must match the type.")
            .WithValidation<RecordRevisionReportRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsField);

        group.MapGet("/revision-reports/{reportId:guid}/file", DownloadRevisionReport)
            .WithName("DownloadInstallationRevisionReport")
            .WithSummary("Download the revision report file (the download is recorded)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapPost("/commission", Commission)
            .WithName("CommissionInstallation")
            .WithSummary("Put the plant into operation with the acceptance protocol and turn monitoring on")
            .WithDescription("Requires state InspectionPassed with a still valid revision and an approved connection request, otherwise 409.")
            .WithValidation<CommissionInstallationRequest>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsField);

        group.MapGet("/documentation", GetDocumentation)
            .WithName("GetInstallationDocumentation")
            .WithSummary("Documentation package: revision report, acceptance protocol, devices and download log")
            .WithDescription("Available from commissioning on, before that 409.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);

        group.MapGet("/documentation/handover-protocol", DownloadHandoverProtocol)
            .WithName("DownloadInstallationHandoverProtocol")
            .WithSummary("Download the hand-over protocol PDF with the device list (the download is recorded)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.InstallationsRead);
    }

    internal static async Task<Ok<ChecklistResponse>> GetChecklist(Guid id, IInstallationWorkflowService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetChecklistAsync(id, ct));

    internal static async Task<Ok<ChecklistResponse>> UpdateChecklistStep(
        Guid id, string stepCode, UpdateChecklistStepRequest request, IInstallationWorkflowService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateChecklistStepAsync(id, stepCode, request, ct));

    internal static async Task<Ok<IReadOnlyList<RevisionReportResponse>>> ListRevisionReports(
        Guid id, IInstallationWorkflowService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListRevisionReportsAsync(id, ct));

    internal static async Task<Ok<RevisionReportResponse>> GetRevisionReport(
        Guid id, Guid reportId, IInstallationWorkflowService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetRevisionReportAsync(id, reportId, ct));

    internal static async Task<Created<RevisionReportResponse>> RecordRevisionReport(
        Guid id, RecordRevisionReportRequest request, IInstallationWorkflowService service, CancellationToken ct)
    {
        var report = await service.RecordRevisionReportAsync(id, request, ct);
        return TypedResults.Created($"/api/installations/{id}/revision-reports/{report.Id}", report);
    }

    internal static async Task<FileContentHttpResult> DownloadRevisionReport(
        Guid id, Guid reportId, IInstallationWorkflowService service, HttpContext http, CancellationToken ct)
    {
        var file = await service.DownloadRevisionReportAsync(id, reportId, ct);
        // Only allow-listed types are stored; still, never let the browser sniff the content into something else.
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }

    internal static async Task<Ok<InstallationDetailResponse>> Commission(
        Guid id, CommissionInstallationRequest request, IInstallationWorkflowService service, CancellationToken ct) =>
        TypedResults.Ok(await service.CommissionAsync(id, request, ct));

    internal static async Task<Ok<InstallationDocumentationResponse>> GetDocumentation(
        Guid id, IInstallationWorkflowService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetDocumentationAsync(id, ct));

    internal static async Task<FileContentHttpResult> DownloadHandoverProtocol(
        Guid id, IInstallationWorkflowService service, HttpContext http, CancellationToken ct)
    {
        var file = await service.DownloadHandoverProtocolAsync(id, ct);
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }
}
