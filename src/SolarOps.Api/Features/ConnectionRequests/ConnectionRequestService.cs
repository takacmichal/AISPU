using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.ConnectionRequests;

public interface IConnectionRequestService
{
    Task<ConnectionRequestResponse> CreateAsync(CreateConnectionRequestRequest request, CancellationToken ct);

    Task<ConnectionRequestResponse> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ConnectionRequestResponse>> ListAsync(
        Guid? installationId, Guid? customerId, ConnectionRequestStatus? status, CancellationToken ct);

    Task<ConnectionRequestResponse> UpdateAsync(Guid id, UpdateConnectionRequestRequest request, CancellationToken ct);

    Task<ConnectionAttachmentFile> DownloadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct);
}

public sealed class ConnectionRequestService(
    AppDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<ConnectionRequestOptions> options) : IConnectionRequestService
{
    public async Task<ConnectionRequestResponse> CreateAsync(CreateConnectionRequestRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installationId = request.InstallationId!.Value;

        var installation = await db.Set<Installation>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == installationId, ct)
            ?? throw new RequestValidationException(
                nameof(CreateConnectionRequestRequest.InstallationId), "Installation does not exist.");

        if (await db.Set<ConnectionRequest>().AnyAsync(r => r.InstallationId == installationId, ct))
        {
            throw new DomainException("The installation already has a connection request.");
        }

        var connectionRequest = ConnectionRequest.Create(
            installation.Id,
            installation.CustomerId,
            installation.SupplyPointId,
            request.TotalInverterAcPowerKw!.Value,
            request.Note,
            ReadAttachments(request.Attachments!),
            options.Value.SmallSourceMaxKw,
            userId,
            timeProvider.GetUtcNow());

        db.Set<ConnectionRequest>().Add(connectionRequest);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request created the connection request of this installation after our pre-check.
            throw new DomainException("The installation already has a connection request.");
        }

        return ConnectionRequestResponse.FromEntity(connectionRequest);
    }

    public async Task<ConnectionRequestResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var connectionRequest = await db.Set<ConnectionRequest>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("ConnectionRequest", id);

        var attachments = await LoadAttachmentMetadataAsync([id], ct);
        return ConnectionRequestResponse.From(connectionRequest, attachments.GetValueOrDefault(id, []));
    }

    public async Task<IReadOnlyList<ConnectionRequestResponse>> ListAsync(
        Guid? installationId, Guid? customerId, ConnectionRequestStatus? status, CancellationToken ct)
    {
        var query = db.Set<ConnectionRequest>().AsNoTracking();
        if (installationId is { } installation)
        {
            query = query.Where(r => r.InstallationId == installation);
        }

        if (customerId is { } customer)
        {
            query = query.Where(r => r.CustomerId == customer);
        }

        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var requests = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToListAsync(ct);
        if (requests.Count == 0)
        {
            return [];
        }

        var attachments = await LoadAttachmentMetadataAsync(requests.Select(r => r.Id).ToList(), ct);
        return requests
            .Select(r => ConnectionRequestResponse.From(r, attachments.GetValueOrDefault(r.Id, [])))
            .ToList();
    }

    public async Task<ConnectionRequestResponse> UpdateAsync(
        Guid id, UpdateConnectionRequestRequest request, CancellationToken ct)
    {
        var connectionRequest = await db.Set<ConnectionRequest>().Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("ConnectionRequest", id);

        connectionRequest.Update(
            request.TotalInverterAcPowerKw!.Value,
            request.Note,
            ReadAttachments(request.Attachments!),
            options.Value.SmallSourceMaxKw,
            timeProvider.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("The connection request was changed by another request. Reload it and try again.");
        }

        return ConnectionRequestResponse.FromEntity(connectionRequest);
    }

    public async Task<ConnectionAttachmentFile> DownloadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await db.Set<ConnectionRequestAttachment>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ConnectionRequestId == id, ct)
            ?? throw new NotFoundException("ConnectionRequestAttachment", attachmentId);

        return new ConnectionAttachmentFile(attachment.Content, attachment.FileName, attachment.ContentType);
    }

    private async Task<Dictionary<Guid, List<ConnectionRequestAttachmentResponse>>> LoadAttachmentMetadataAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct)
    {
        var rows = await db.Set<ConnectionRequestAttachment>().AsNoTracking()
            .Where(a => requestIds.Contains(a.ConnectionRequestId))
            .OrderBy(a => a.Id)
            .Select(a => new { a.ConnectionRequestId, a.Id, a.Kind, a.FileName, a.ContentType, a.SizeBytes })
            .ToListAsync(ct);

        return rows
            .GroupBy(a => a.ConnectionRequestId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(a => new ConnectionRequestAttachmentResponse(
                        a.Id,
                        a.Kind,
                        a.FileName,
                        a.ContentType,
                        a.SizeBytes,
                        ConnectionRequestAttachmentResponse.BuildDownloadUrl(a.ConnectionRequestId, a.Id)))
                    .ToList());
    }

    private static IReadOnlyList<ConnectionAttachmentData> ReadAttachments(
        IReadOnlyList<ConnectionRequestAttachmentRequest> attachments)
    {
        var result = new List<ConnectionAttachmentData>(attachments.Count);
        var errors = new Dictionary<string, string[]>();

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            byte[] content;
            try
            {
                content = Convert.FromBase64String(attachment.ContentBase64!);
            }
            catch (FormatException)
            {
                errors[$"Attachments[{i}].ContentBase64"] = ["The ContentBase64 field is not valid base64."];
                continue;
            }

            if (content.Length == 0)
            {
                errors[$"Attachments[{i}].ContentBase64"] = ["The attachment is empty."];
                continue;
            }

            if (content.LongLength > ConnectionRequest.MaxAttachmentBytes)
            {
                errors[$"Attachments[{i}].ContentBase64"] =
                    [$"An attachment can be at most {ConnectionRequest.MaxAttachmentBytes / (1024 * 1024)} MB."];
                continue;
            }

            result.Add(new ConnectionAttachmentData(
                attachment.Kind!.Value,
                attachment.FileName!.Trim(),
                string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType.Trim(),
                content));
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return result;
    }
}
