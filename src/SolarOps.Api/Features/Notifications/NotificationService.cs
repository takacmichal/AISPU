using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Notifications;

public interface INotificationService
{
    Task<NotificationListResponse> ListAsync(CancellationToken ct);
}

/// <summary>
/// The feed is computed from ticket history and connection request states, so it needs no table of its own. A customer gets
/// only own records (<see cref="ICustomerScope"/>), a technician only assigned tickets, and nobody is told about their own ticket changes.
/// </summary>
internal sealed class NotificationService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICustomerScope customerScope,
    TimeProvider time) : INotificationService
{
    public const int WindowDays = 30;
    public const int MaxItems = 20;

    public async Task<NotificationListResponse> ListAsync(CancellationToken ct)
    {
        var since = time.GetUtcNow().AddDays(-WindowDays);
        var scope = await customerScope.GetAsync(ct);
        var isTechnicianOnly = currentUser.IsInRole(Roles.Technician) && !currentUser.IsInRole(Roles.Admin) && !currentUser.IsInRole(Roles.Operator);

        var tickets = await TicketItemsAsync(since, scope, isTechnicianOnly, ct);
        var connectionRequests = isTechnicianOnly ? [] : await ConnectionRequestItemsAsync(since, scope, ct);

        return new NotificationListResponse(
            [.. tickets.Concat(connectionRequests).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal).Take(MaxItems)]);
    }

    private async Task<List<NotificationItemResponse>> TicketItemsAsync(
        DateTimeOffset since, DataScope scope, bool assignedOnly, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var visible = db.Set<ServiceTicket>().AsNoTracking();
        if (scope.IsRestricted)
        {
            visible = visible.Where(x => x.CustomerId == scope.OwnCustomerId);
        }

        if (assignedOnly)
        {
            visible = visible.Where(x => db.Set<ServiceTicketAssignment>().Any(a => a.ServiceTicketId == x.Id && a.TechnicianId == userId));
        }

        var changes = await (
                from change in db.Set<ServiceTicketStateChange>().AsNoTracking()
                join ticket in visible on change.ServiceTicketId equals ticket.Id
                where change.ChangedAt >= since && change.ChangedBy != userId
                orderby change.ChangedAt descending, change.Id
                select new { change.Id, TicketId = ticket.Id, ticket.Title, change.ToState, change.ChangedAt })
            .Take(MaxItems)
            .ToListAsync(ct);

        return changes.ConvertAll(x => new NotificationItemResponse(
            $"ticket:{x.Id}",
            NotificationKind.Ticket,
            $"{x.Title}: {NotificationTexts.Label(x.ToState)}",
            x.ChangedAt,
            $"/tickets/{x.TicketId}"));
    }

    private async Task<List<NotificationItemResponse>> ConnectionRequestItemsAsync(DateTimeOffset since, DataScope scope, CancellationToken ct)
    {
        var requests = db.Set<ConnectionRequest>().AsNoTracking()
            .Where(x => x.State != ConnectionRequestState.Draft && x.UpdatedAt >= since);
        if (scope.IsRestricted)
        {
            requests = requests.Where(x => x.CustomerId == scope.OwnCustomerId);
        }

        var latest = await requests
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Take(MaxItems)
            .Select(x => new { x.Id, x.EicCode, x.State, x.UpdatedAt })
            .ToListAsync(ct);

        return latest.ConvertAll(x => new NotificationItemResponse(
            // One id per state: a new state is a new notification, an edit within the same state is not.
            string.Create(CultureInfo.InvariantCulture, $"connection_request:{x.Id}:{x.State}"),
            NotificationKind.ConnectionRequest,
            $"Žiadosť o pripojenie {x.EicCode}: {NotificationTexts.Label(x.State)}",
            x.UpdatedAt,
            $"/connection-requests/{x.Id}"));
    }
}
