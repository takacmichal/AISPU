using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Installations;

/// <summary>A stored or generated document returned by a download endpoint.</summary>
public sealed record InstallationDocumentFile(byte[] Content, string ContentType, string FileName);

public interface IInstallationWorkflowService
{
    Task<ChecklistResponse> GetChecklistAsync(Guid id, CancellationToken ct);

    Task<ChecklistResponse> UpdateChecklistStepAsync(Guid id, string stepCode, UpdateChecklistStepRequest request, CancellationToken ct);

    Task<IReadOnlyList<RevisionReportResponse>> ListRevisionReportsAsync(Guid id, CancellationToken ct);

    Task<RevisionReportResponse> GetRevisionReportAsync(Guid id, Guid reportId, CancellationToken ct);

    Task<RevisionReportResponse> RecordRevisionReportAsync(Guid id, RecordRevisionReportRequest request, CancellationToken ct);

    Task<InstallationDocumentFile> DownloadRevisionReportAsync(Guid id, Guid reportId, CancellationToken ct);

    Task<InstallationDetailResponse> CommissionAsync(Guid id, CommissionInstallationRequest request, CancellationToken ct);

    Task<InstallationDocumentationResponse> GetDocumentationAsync(Guid id, CancellationToken ct);

    Task<InstallationDocumentFile> DownloadHandoverProtocolAsync(Guid id, CancellationToken ct);
}

/// <summary>Assembly checklist (#90), revision report (#91), commissioning (#92) and documentation hand-over (#93).</summary>
internal sealed class InstallationWorkflowService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICustomerScope customerScope,
    TimeProvider time,
    IHandoverProtocolPdfGenerator pdfGenerator) : IInstallationWorkflowService
{
    private const string UniqueViolation = "23505";

    public async Task<ChecklistResponse> GetChecklistAsync(Guid id, CancellationToken ct)
    {
        var installation = await (await VisibleAsync(ct))
            .Include(x => x.ChecklistItems)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Installation", id);

        return ChecklistResponse.From(installation);
    }

    public async Task<ChecklistResponse> UpdateChecklistStepAsync(Guid id, string stepCode, UpdateChecklistStepRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installation = await LoadAsync(id, ct);
        installation.SetChecklistStep(stepCode, request.Done!.Value, request.Note, userId, time.GetUtcNow());
        await SaveAsync("The checklist step was changed by another request. Reload the checklist and try again.", ct);
        return ChecklistResponse.From(installation);
    }

    public async Task<IReadOnlyList<RevisionReportResponse>> ListRevisionReportsAsync(Guid id, CancellationToken ct)
    {
        await EnsureVisibleAsync(id, ct);
        var reports = await ReportsOf(id).OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).Select(ToResponse).ToListAsync(ct);
        return reports;
    }

    public async Task<RevisionReportResponse> GetRevisionReportAsync(Guid id, Guid reportId, CancellationToken ct)
    {
        await EnsureVisibleAsync(id, ct);
        return await ReportsOf(id).Where(x => x.Id == reportId).Select(ToResponse).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Revision report", reportId);
    }

    public async Task<RevisionReportResponse> RecordRevisionReportAsync(Guid id, RecordRevisionReportRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installation = await LoadAsync(id, ct);
        var content = DecodeFile(request.ContentType!, request.ContentBase64!);

        var report = RevisionReport.Create(
            installation.Id,
            request.InspectedOn!.Value,
            request.InspectorName!,
            request.InspectorCertificateNumber,
            request.Result!.Value,
            request.ValidUntil,
            request.Findings,
            request.FileName!,
            request.ContentType!,
            content,
            userId,
            time.GetUtcNow());

        installation.RecordRevision(report, userId, report.RecordedAt);
        db.Add(report);
        await SaveAsync("The installation was changed by another request.", ct);
        return RevisionReportResponse.From(report);
    }

    public async Task<InstallationDocumentFile> DownloadRevisionReportAsync(Guid id, Guid reportId, CancellationToken ct)
    {
        await EnsureVisibleAsync(id, ct);
        var file = await db.Set<RevisionReport>().AsNoTracking()
            .Where(x => x.Id == reportId && x.InstallationId == id)
            .Select(x => new InstallationDocumentFile(x.Content, x.ContentType, x.FileName))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Revision report", reportId);

        await RecordDownloadAsync(id, InstallationDocumentKind.RevisionReport, reportId, ct);
        return file;
    }

    public async Task<InstallationDetailResponse> CommissionAsync(Guid id, CommissionInstallationRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installation = await LoadAsync(id, ct);
        var approved = await ConnectionApproval.IsApprovedAsync(db, id, ct);

        installation.Commission(request.CommissionedOn!.Value, request.AcceptedBy!, request.Note, approved, userId, time.GetUtcNow());
        await SaveAsync("The installation was changed by another request.", ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDocumentationResponse> GetDocumentationAsync(Guid id, CancellationToken ct)
    {
        var (installation, report) = await LoadDocumentedAsync(id, ct);

        var devices = await InstalledDevicesAsync(id, ct);
        var downloads = await db.Set<InstallationDocumentDownload>().AsNoTracking()
            .Where(x => x.InstallationId == id)
            .OrderBy(x => x.DownloadedAt).ThenBy(x => x.Id)
            .Select(x => new DocumentDownloadResponse(x.Kind, x.DocumentId, x.DownloadedBy, x.DownloadedAt))
            .ToListAsync(ct);

        return new InstallationDocumentationResponse(
            installation.Id,
            installation.Name,
            installation.State,
            new CommissioningProtocolResponse(
                installation.CommissionedOn!.Value,
                installation.HandoverAcceptedBy ?? string.Empty,
                installation.CommissioningNote,
                installation.CommissionedBy ?? Guid.Empty,
                installation.MonitoringEnabled,
                installation.MonitoringEnabledAt),
            report,
            devices.Select(d => new DocumentationDeviceResponse(d.Type, d.SerialNumber, d.Manufacturer, d.Model, d.PowerW)).ToList(),
            downloads);
    }

    public async Task<InstallationDocumentFile> DownloadHandoverProtocolAsync(Guid id, CancellationToken ct)
    {
        var (installation, report) = await LoadDocumentedAsync(id, ct);
        var devices = await InstalledDevicesAsync(id, ct);
        // DisplayName is computed in C#, so the customer row is loaded (installations may also exist without a customer row).
        var customerName = (await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == installation.CustomerId, ct))?.DisplayName;
        var handedOverAt = installation.StateChanges
            .Where(c => c.ToState == InstallationState.HandedOver)
            .Select(c => (DateTimeOffset?)c.ChangedAt)
            .LastOrDefault();

        var model = new HandoverProtocolPdfModel(
            installation.Name,
            installation.Id.ToString(),
            customerName ?? "neuvedený",
            HandoverProtocolPdfModel.Date(installation.CommissionedOn!.Value),
            handedOverAt is { } at ? HandoverProtocolPdfModel.Date(DateOnly.FromDateTime(at.UtcDateTime)) : null,
            installation.HandoverAcceptedBy ?? string.Empty,
            installation.CommissioningNote,
            HandoverProtocolPdfModel.Date(report.InspectedOn),
            report.InspectorCertificateNumber is { } certificate ? $"{report.InspectorName} ({certificate})" : report.InspectorName,
            report.Result == RevisionResult.Passed ? "vyhovuje" : "nevyhovuje",
            report.ValidUntil is { } validUntil ? HandoverProtocolPdfModel.Date(validUntil) : null,
            report.FileName,
            report.Sha256,
            devices.Select((d, i) => new HandoverProtocolDeviceLine(
                    i + 1, HandoverProtocolPdfModel.DeviceType(d.Type), d.Manufacturer, d.Model, d.SerialNumber, HandoverProtocolPdfModel.Power(d.PowerW)))
                .ToList(),
            HandoverProtocolPdfModel.Date(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)));

        var content = pdfGenerator.Generate(model);
        await RecordDownloadAsync(id, InstallationDocumentKind.HandoverProtocol, null, ct);
        return new InstallationDocumentFile(content, "application/pdf", $"preberaci-protokol-{installation.Id}.pdf");
    }

    // The package exists from commissioning on (#93: documentation after the plant is put into operation).
    private async Task<(Installation Installation, RevisionReportResponse Report)> LoadDocumentedAsync(Guid id, CancellationToken ct)
    {
        var installation = await (await VisibleAsync(ct))
            .Include(x => x.StateChanges)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Installation", id);

        if (installation.State is not (InstallationState.Commissioned or InstallationState.HandedOver) || installation.CommissionedOn is null)
        {
            throw new DomainException($"The documentation package is available after commissioning (state is {installation.State}).");
        }

        var reportId = installation.LastRevisionReportId
            ?? throw new DomainException("The installation has no revision report.");
        var report = await ReportsOf(id).Where(x => x.Id == reportId).Select(ToResponse).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("The installation has no revision report.");
        return (installation, report);
    }

    private Task<List<InstallationDevice>> InstalledDevicesAsync(Guid id, CancellationToken ct) =>
        db.Set<InstallationDevice>().AsNoTracking()
            .Where(d => d.InstallationId == id && d.Status == DeviceStatus.Installed)
            .OrderBy(d => d.Type).ThenBy(d => d.SerialNumber)
            .ToListAsync(ct);

    private IQueryable<RevisionReport> ReportsOf(Guid installationId) =>
        db.Set<RevisionReport>().AsNoTracking().Where(x => x.InstallationId == installationId);

    // Metadata only: the file bytes stay in the database unless the download endpoint asks for them.
    private static readonly Expression<Func<RevisionReport, RevisionReportResponse>> ToResponse = x =>
        new RevisionReportResponse(
            x.Id, x.InstallationId, x.InspectedOn, x.InspectorName, x.InspectorCertificateNumber, x.Result, x.ValidUntil, x.Findings,
            x.FileName, x.ContentType, x.SizeBytes, x.Sha256, x.RecordedBy, x.RecordedAt);

    private async Task RecordDownloadAsync(Guid installationId, InstallationDocumentKind kind, Guid? documentId, CancellationToken ct)
    {
        db.Add(InstallationDocumentDownload.Record(installationId, kind, documentId, currentUser.RequireUserId(), time.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    private static byte[] DecodeFile(string contentType, string base64)
    {
        const string field = nameof(RecordRevisionReportRequest.ContentBase64);
        var buffer = new byte[(base64.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(base64.Trim(), buffer, out var written))
        {
            throw new RequestValidationException(field, "The ContentBase64 field is not valid base64.");
        }

        if (written == 0)
        {
            throw new RequestValidationException(field, "The file is empty.");
        }

        if (written > RevisionReport.MaxFileBytes)
        {
            throw new RequestValidationException(field, $"The file can be at most {RevisionReport.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var content = buffer[..written];
        if (!RevisionReport.HasValidSignature(contentType, content))
        {
            throw new RequestValidationException(field, $"The file content does not match the content type '{contentType.Trim()}'.");
        }

        return content;
    }

    private async Task<IQueryable<Installation>> VisibleAsync(CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var query = db.Set<Installation>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            query = query.Where(x => x.CustomerId == own);
        }

        return query;
    }

    // Another customer's installation looks exactly like a missing one.
    private async Task EnsureVisibleAsync(Guid id, CancellationToken ct)
    {
        if (!await (await VisibleAsync(ct)).AnyAsync(x => x.Id == id, ct))
        {
            throw new NotFoundException("Installation", id);
        }
    }

    // Write endpoints are not open to customers (policy InstallationsField), so no customer scope is needed here.
    private async Task<Installation> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<Installation>()
            .Include(x => x.StateChanges)
            .Include(x => x.ChecklistItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundException("Installation", id);

    private async Task SaveAsync(string conflictMessage, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Two requests created the same checklist step at once (unique installation_id + step_code).
            throw new DomainException(conflictMessage);
        }
    }
}
