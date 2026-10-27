using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.Customers;
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
        Guid? installationId, Guid? customerId, ConnectionRequestState? state, CancellationToken ct);

    Task<ConnectionRequestResponse> UpdateAsync(Guid id, UpdateConnectionRequestRequest request, CancellationToken ct);

    Task<ConnectionAttachmentFile> DownloadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct);
}

internal sealed class ConnectionRequestService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICustomerScope customerScope,
    IOptions<ConnectionRequestOptions> options,
    TimeProvider time) : IConnectionRequestService
{
    public async Task<ConnectionRequestResponse> CreateAsync(CreateConnectionRequestRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installationId = request.InstallationId!.Value;

        // Customer and supply point come from the installation, so the request cannot point at a foreign supply point.
        var installation = await db.Set<Installation>().AsNoTracking()
            .Where(x => x.Id == installationId)
            .Select(x => new { x.CustomerId, x.SupplyPointId })
            .FirstOrDefaultAsync(ct)
            ?? throw new RequestValidationException(nameof(CreateConnectionRequestRequest.InstallationId), "Installation does not exist.");

        var connectionRequest = ConnectionRequest.Create(
            installationId,
            installation.CustomerId,
            installation.SupplyPointId,
            request.EicCode!,
            request.RatedAcPowerKw,
            options.Value.SmallSourceMaxKw,
            request.MainBreakerAmps,
            ReadAttachments(request.Attachments!),
            userId,
            time.GetUtcNow());

        db.Add(connectionRequest);
        await db.SaveChangesAsync(ct);
        return ConnectionRequestResponse.From(connectionRequest);
    }

    public async Task<ConnectionRequestResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var connectionRequest = await Visible(scope).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Connection request", id);

        var attachments = await LoadAttachmentMetadataAsync([id], ct);
        return ConnectionRequestResponse.From(connectionRequest, attachments.GetValueOrDefault(id, []));
    }

    public async Task<IReadOnlyList<ConnectionRequestResponse>> ListAsync(
        Guid? installationId, Guid? customerId, ConnectionRequestState? state, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var query = Visible(scope).AsNoTracking();

        if (installationId is { } installation)
        {
            query = query.Where(x => x.InstallationId == installation);
        }

        if (customerId is { } customer)
        {
            query = query.Where(x => x.CustomerId == customer);
        }

        if (state is { } s)
        {
            query = query.Where(x => x.State == s);
        }

        var requests = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
        if (requests.Count == 0)
        {
            return [];
        }

        var attachments = await LoadAttachmentMetadataAsync(requests.Select(x => x.Id).ToList(), ct);
        return requests
            .Select(x => ConnectionRequestResponse.From(x, attachments.GetValueOrDefault(x.Id, [])))
            .ToList();
    }

    public async Task<ConnectionRequestResponse> UpdateAsync(
        Guid id, UpdateConnectionRequestRequest request, CancellationToken ct)
    {
        var connectionRequest = await db.Set<ConnectionRequest>().Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Connection request", id);

        connectionRequest.Update(
            request.EicCode!,
            request.RatedAcPowerKw,
            options.Value.SmallSourceMaxKw,
            request.MainBreakerAmps,
            ReadAttachments(request.Attachments!),
            time.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("The connection request was changed by another request. Reload it and try again.");
        }

        return ConnectionRequestResponse.From(connectionRequest);
    }

    public async Task<ConnectionAttachmentFile> DownloadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        if (!await Visible(scope).AsNoTracking().AnyAsync(x => x.Id == id, ct))
        {
            throw new NotFoundException("Connection request", id);
        }

        var attachment = await db.Set<ConnectionRequestAttachment>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.ConnectionRequestId == id, ct)
            ?? throw new NotFoundException("Connection request attachment", attachmentId);

        if (attachment.Content is not { Length: > 0 } content)
        {
            throw new NotFoundException("Connection request attachment content", attachmentId);
        }

        return new ConnectionAttachmentFile(content, attachment.FileName, attachment.ContentType);
    }

    private IQueryable<ConnectionRequest> Visible(DataScope scope)
    {
        var query = db.Set<ConnectionRequest>();
        return scope.IsRestricted ? query.Where(x => x.CustomerId == scope.OwnCustomerId) : query;
    }

    private async Task<Dictionary<Guid, List<AttachmentResponse>>> LoadAttachmentMetadataAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct)
    {
        var rows = await db.Set<ConnectionRequestAttachment>().AsNoTracking()
            .Where(x => requestIds.Contains(x.ConnectionRequestId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new { x.ConnectionRequestId, x.Id, x.Kind, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt, x.HasContent })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ConnectionRequestId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(x => AttachmentResponse.FromMetadata(
                        x.ConnectionRequestId, x.Id, x.Kind, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt, x.HasContent))
                    .ToList());
    }

    private static IReadOnlyList<AttachmentInput> ReadAttachments(IReadOnlyList<AttachmentRequest> attachments)
    {
        var result = new List<AttachmentInput>(attachments.Count);
        var errors = new Dictionary<string, string[]>();

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            byte[]? content = null;

            if (!string.IsNullOrWhiteSpace(attachment.ContentBase64))
            {
                // Decode into a buffer sized from the string length (already capped by MaxLength) instead of
                // letting Convert.FromBase64String allocate and throw on bad input.
                var encoded = attachment.ContentBase64;
                var buffer = new byte[(encoded.Length / 4) * 3];

                if (!Convert.TryFromBase64String(encoded, buffer, out var written))
                {
                    errors[$"Attachments[{i}].ContentBase64"] = ["The ContentBase64 field is not valid base64."];
                    continue;
                }

                if (written == 0)
                {
                    errors[$"Attachments[{i}].ContentBase64"] = ["The attachment is empty."];
                    continue;
                }

                if (written > ConnectionRequest.MaxAttachmentBytes)
                {
                    errors[$"Attachments[{i}].ContentBase64"] =
                        [$"An attachment can be at most {ConnectionRequest.MaxAttachmentBytes / (1024 * 1024)} MB."];
                    continue;
                }

                content = buffer[..written];
            }

            var sizeBytes = content?.LongLength ?? attachment.SizeBytes;
            if (sizeBytes <= 0)
            {
                errors[$"Attachments[{i}].SizeBytes"] = ["SizeBytes must be greater than 0 when no file content is sent."];
                continue;
            }

            result.Add(new AttachmentInput(
                attachment.Kind!.Value,
                attachment.FileName!,
                attachment.ContentType!,
                sizeBytes,
                content));
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return result;
    }
}
