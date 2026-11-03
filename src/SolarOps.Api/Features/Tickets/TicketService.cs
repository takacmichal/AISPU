using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Tickets;

/// <summary>Filters and paging of <c>GET /api/tickets</c>; every filter is optional and they combine with AND.</summary>
public sealed record TicketListQuery(
    ServiceTicketState? State,
    ServiceTicketPriority? Priority,
    ServiceTicketOrigin? Origin,
    Guid? InstallationId,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDir);

public interface ITicketService
{
    Task<TicketResponse> CreateAsync(CreateTicketRequest request, CancellationToken ct);

    Task<TicketResponse> GetAsync(Guid id, CancellationToken ct);

    Task<TicketListResponse> ListAsync(TicketListQuery query, CancellationToken ct);

    /// <summary>Sets the priority and moves New to Triaged.</summary>
    Task<TicketResponse> TriageAsync(Guid id, TriageTicketRequest request, CancellationToken ct);

    /// <summary>Back-office transition; states of the visit workflow (#36, #37) are refused with 409.</summary>
    Task<TicketResponse> TransitionAsync(Guid id, TransitionTicketRequest request, CancellationToken ct);

    Task<IReadOnlyList<TicketStateChangeResponse>> HistoryAsync(Guid id, CancellationToken ct);

    Task<TicketCommentResponse> AddCommentAsync(Guid id, AddTicketCommentRequest request, CancellationToken ct);

    Task<IReadOnlyList<TicketCommentResponse>> ListCommentsAsync(Guid id, CancellationToken ct);

    Task<TicketAttachmentFile> DownloadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct);
}

internal sealed class TicketService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICustomerScope customerScope,
    TimeProvider time) : ITicketService
{
    public const int MaxPageSize = 100;

    public async Task<TicketResponse> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var scope = await customerScope.GetAsync(ct);
        var installationId = request.InstallationId!.Value;

        // A customer may report only for its own installation; a foreign one answers exactly like a missing one.
        var installations = db.Set<Installation>().AsNoTracking().Where(x => x.Id == installationId);
        if (scope.IsRestricted)
        {
            installations = installations.Where(x => x.CustomerId == scope.OwnCustomerId);
        }

        var customerId = await installations.Select(x => (Guid?)x.CustomerId).FirstOrDefaultAsync(ct)
            ?? throw new RequestValidationException(nameof(CreateTicketRequest.InstallationId), "Installation does not exist.");

        var ticket = ServiceTicket.Create(
            installationId,
            customerId,
            request.Title!,
            request.Description!,
            scope.IsRestricted ? ServiceTicketPriority.Medium : request.Priority ?? ServiceTicketPriority.Medium,
            scope.IsRestricted ? ServiceTicketOrigin.CustomerReport : ServiceTicketOrigin.Manual,
            ReadAttachments(request.Attachments ?? []),
            userId,
            time.GetUtcNow());

        db.Add(ticket);
        await db.SaveChangesAsync(ct);
        return TicketResponse.From(ticket);
    }

    public async Task<TicketResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var ticket = await Visible(scope).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Service ticket", id);

        return TicketResponse.From(ticket, await LoadAttachmentMetadataAsync(id, ct));
    }

    public async Task<TicketListResponse> ListAsync(TicketListQuery query, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        }

        var sortKey = (query.SortBy ?? "createdAt").ToLowerInvariant();
        if (sortKey is not ("createdat" or "updatedat" or "priority"))
        {
            errors["sortBy"] = ["SortBy must be one of: createdAt, updatedAt, priority."];
        }

        var direction = (query.SortDir ?? "desc").ToLowerInvariant();
        if (direction is not ("asc" or "desc"))
        {
            errors["sortDir"] = ["SortDir must be asc or desc."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var scope = await customerScope.GetAsync(ct);
        var tickets = Visible(scope).AsNoTracking();
        if (query.State is { } state)
        {
            tickets = tickets.Where(x => x.State == state);
        }

        if (query.Priority is { } priority)
        {
            tickets = tickets.Where(x => x.Priority == priority);
        }

        if (query.Origin is { } origin)
        {
            tickets = tickets.Where(x => x.Origin == origin);
        }

        if (query.InstallationId is { } installationId)
        {
            tickets = tickets.Where(x => x.InstallationId == installationId);
        }

        var total = await tickets.CountAsync(ct);
        var descending = direction == "desc";

        // Priority is stored as text, so it is ranked explicitly; "desc" puts High first.
        var ordered = sortKey switch
        {
            "updatedat" => descending ? tickets.OrderByDescending(x => x.UpdatedAt) : tickets.OrderBy(x => x.UpdatedAt),
            "priority" => descending
                ? tickets.OrderByDescending(x => x.Priority == ServiceTicketPriority.High ? 2 : x.Priority == ServiceTicketPriority.Medium ? 1 : 0)
                    .ThenByDescending(x => x.CreatedAt)
                : tickets.OrderBy(x => x.Priority == ServiceTicketPriority.High ? 2 : x.Priority == ServiceTicketPriority.Medium ? 1 : 0)
                    .ThenByDescending(x => x.CreatedAt),
            _ => descending ? tickets.OrderByDescending(x => x.CreatedAt) : tickets.OrderBy(x => x.CreatedAt),
        };

        var items = await ordered
            .ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItem(
                x.Id, x.InstallationId, x.CustomerId, x.Title, x.Priority, x.Origin, x.State, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);

        return new TicketListResponse(items, query.Page, query.PageSize, total);
    }

    public Task<TicketResponse> TriageAsync(Guid id, TriageTicketRequest request, CancellationToken ct) =>
        ChangeStateAsync(id, (ticket, userId, now) => ticket.Triage(request.Priority!.Value, userId, request.Note, now), ct);

    public Task<TicketResponse> TransitionAsync(Guid id, TransitionTicketRequest request, CancellationToken ct)
    {
        var target = request.State!.Value;
        return ChangeStateAsync(id, (ticket, userId, now) =>
        {
            if (ServiceTicket.VisitWorkflowStates.Contains(target))
            {
                throw new DomainException(
                    $"State {target} is set by technician visits and the intervention record (#36, #37), not by a manual transition.");
            }

            ticket.TransitionTo(target, userId, request.Note, now);
        }, ct);
    }

    public async Task<IReadOnlyList<TicketStateChangeResponse>> HistoryAsync(Guid id, CancellationToken ct)
    {
        var scope = await EnsureVisibleAsync(id, ct);
        var changes = await db.Set<ServiceTicketStateChange>().AsNoTracking()
            .Where(x => x.ServiceTicketId == id)
            .OrderBy(x => x.ChangedAt).ThenBy(x => x.Id)
            .ToListAsync(ct);

        // Transition notes are internal back-office notes; the customer sees states and times only.
        return changes.ConvertAll(x => new TicketStateChangeResponse(
            x.Id, x.FromState, x.ToState, x.ChangedBy, x.ChangedAt, scope.IsRestricted ? null : x.Note));
    }

    public async Task<TicketCommentResponse> AddCommentAsync(Guid id, AddTicketCommentRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await EnsureVisibleAsync(id, ct);

        var comment = ServiceTicketComment.Create(id, userId, request.Body!, request.IsPublic, time.GetUtcNow());
        db.Add(comment);
        await db.SaveChangesAsync(ct);
        return TicketCommentResponse.From(comment);
    }

    public async Task<IReadOnlyList<TicketCommentResponse>> ListCommentsAsync(Guid id, CancellationToken ct)
    {
        var scope = await EnsureVisibleAsync(id, ct);
        var comments = db.Set<ServiceTicketComment>().AsNoTracking().Where(x => x.ServiceTicketId == id);
        if (scope.IsRestricted)
        {
            comments = comments.Where(x => x.IsPublic);
        }

        var list = await comments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return list.ConvertAll(TicketCommentResponse.From);
    }

    public async Task<TicketAttachmentFile> DownloadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        await EnsureVisibleAsync(id, ct);
        var attachment = await db.Set<ServiceTicketAttachment>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.ServiceTicketId == id, ct)
            ?? throw new NotFoundException("Service ticket attachment", attachmentId);

        return new TicketAttachmentFile(attachment.Content, attachment.FileName, attachment.ContentType);
    }

    private async Task<TicketResponse> ChangeStateAsync(
        Guid id, Action<ServiceTicket, Guid, DateTimeOffset> change, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var scope = await customerScope.GetAsync(ct);
        var ticket = await Visible(scope).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Service ticket", id);

        change(ticket, userId, time.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("The service ticket was changed by another request. Reload it and try again.");
        }

        return TicketResponse.From(ticket, await LoadAttachmentMetadataAsync(id, ct));
    }

    /// <summary>Attachment metadata only: the bytes stay in the database until downloaded.</summary>
    private async Task<List<TicketAttachmentResponse>> LoadAttachmentMetadataAsync(Guid id, CancellationToken ct)
    {
        var rows = await db.Set<ServiceTicketAttachment>().AsNoTracking()
            .Where(x => x.ServiceTicketId == id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt })
            .ToListAsync(ct);
        return rows.ConvertAll(a => TicketAttachments.ToResponse(id, a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt));
    }

    private async Task<DataScope> EnsureVisibleAsync(Guid id, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        if (!await Visible(scope).AnyAsync(x => x.Id == id, ct))
        {
            throw new NotFoundException("Service ticket", id);
        }

        return scope;
    }

    private IQueryable<ServiceTicket> Visible(DataScope scope)
    {
        var query = db.Set<ServiceTicket>();
        return scope.IsRestricted ? query.Where(x => x.CustomerId == scope.OwnCustomerId) : query;
    }

    private static List<TicketAttachmentInput> ReadAttachments(IReadOnlyList<TicketAttachmentRequest> attachments)
    {
        var result = new List<TicketAttachmentInput>(attachments.Count);
        var errors = new Dictionary<string, string[]>();

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            var key = $"Attachments[{i}].ContentBase64";

            // Decode into a buffer sized from the string length (capped by MaxLength) instead of letting
            // Convert.FromBase64String allocate and throw on bad input.
            var encoded = attachment.ContentBase64!;
            var buffer = new byte[(encoded.Length / 4) * 3];
            if (!Convert.TryFromBase64String(encoded, buffer, out var written))
            {
                errors[key] = ["The ContentBase64 field is not valid base64."];
                continue;
            }

            if (written == 0)
            {
                errors[key] = ["The attachment is empty."];
                continue;
            }

            if (written > ServiceTicket.MaxAttachmentBytes)
            {
                errors[key] = [$"An attachment can be at most {ServiceTicket.MaxAttachmentBytes / (1024 * 1024)} MB."];
                continue;
            }

            result.Add(new TicketAttachmentInput(attachment.FileName!, attachment.ContentType!, buffer[..written]));
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return result;
    }
}
