using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Notifications;

/// <summary>
/// Computes the notifications on read from existing data (no table, no job). The union over all roles of the user is
/// taken and de-duplicated by id; staff sources come first, so a request seen both as staff and as customer keeps the staff wording.
/// Each source is limited to the newest <see cref="MaxItems"/> rows in the database, which is enough for the final top list.
/// </summary>
internal sealed class NotificationService(AppDbContext db, ICurrentUser currentUser, ICustomerScope customerScope) : INotificationService
{
    public const int MaxItems = 50;

    private const string UnknownCustomerName = "neznámy zákazník";

    /// <summary>Row of a connection request before the customer name is attached.</summary>
    private sealed record ConnectionRequestRow(Guid Id, Guid CustomerId, ConnectionRequestState State, DateTimeOffset CreatedAt, bool ForCustomer);

    /// <summary>Time the ticket entered its current state: the last state change (there is always one), created time as fallback.</summary>
    private static readonly Expression<Func<ServiceTicket, DateTimeOffset>> StateEnteredAt =
        t => t.StateChanges.Max(c => (DateTimeOffset?)c.ChangedAt) ?? t.CreatedAt;

    public async Task<NotificationListResponse> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var isStaffTickets = currentUser.IsInRole(Roles.Admin) || currentUser.IsInRole(Roles.Operator);
        var isStaffRequests = isStaffTickets || currentUser.IsInRole(Roles.Sales);
        var isDesigner = currentUser.IsInRole(Roles.Admin) || currentUser.IsInRole(Roles.Designer);
        var isCustomer = currentUser.IsInRole(Roles.Customer);

        var items = new List<NotificationItemResponse>();
        var requests = new List<ConnectionRequestRow>();

        if (isStaffTickets)
        {
            items.AddRange(await TicketItemsAsync(t => t.State == ServiceTicketState.New || t.State == ServiceTicketState.Triaged, ct));
        }

        if (currentUser.IsInRole(Roles.Technician))
        {
            // Only the technician's own active visits; a cancelled visit of a rescheduled ticket no longer counts.
            var ownVisitTickets = db.Set<ServiceVisit>().AsNoTracking()
                .Where(v => v.TechnicianId == userId && (v.State == ServiceVisitState.Planned || v.State == ServiceVisitState.InProgress))
                .Select(v => v.ServiceTicketId);
            items.AddRange(await TicketItemsAsync(
                t => (t.State == ServiceTicketState.VisitScheduled || t.State == ServiceTicketState.OnSite) && ownVisitTickets.Contains(t.Id),
                ct));
        }

        if (isCustomer)
        {
            // OwnCustomerId is Guid.Empty when the account has no linked customer or other roles, so nothing matches.
            var own = (await customerScope.GetAsync(ct)).OwnCustomerId;
            items.AddRange(await TicketItemsAsync(t => t.State == ServiceTicketState.Resolved && t.CustomerId == own, ct));
        }

        if (isStaffRequests)
        {
            requests.AddRange(await RequestRowsAsync(r => r.State == ConnectionRequestState.Submitted, forCustomer: false, ct));
        }

        if (isDesigner)
        {
            requests.AddRange(await RequestRowsAsync(r => r.State == ConnectionRequestState.InfoRequested, forCustomer: false, ct));
        }

        if (isCustomer)
        {
            var own = (await customerScope.GetAsync(ct)).OwnCustomerId;
            requests.AddRange(await RequestRowsAsync(
                r => r.State == ConnectionRequestState.InfoRequested && r.CustomerId == own, forCustomer: true, ct));
        }

        var names = await CustomerNamesAsync(requests.Select(r => r.CustomerId).Distinct().ToList(), ct);
        items.AddRange(requests.Select(r => NotificationMapping.ConnectionRequest(
            r.Id, r.State, names.GetValueOrDefault(r.CustomerId, UnknownCustomerName), r.CreatedAt, r.ForCustomer)));

        var newest = items
            .DistinctBy(i => i.Id, StringComparer.Ordinal)
            .OrderByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Take(MaxItems)
            .ToList();
        return new NotificationListResponse(newest);
    }

    private async Task<List<NotificationItemResponse>> TicketItemsAsync(Expression<Func<ServiceTicket, bool>> filter, CancellationToken ct)
    {
        var rows = await db.Set<ServiceTicket>().AsNoTracking()
            .Where(filter)
            .OrderByDescending(StateEnteredAt).ThenBy(t => t.Id)
            .Take(MaxItems)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.State,
                EnteredAt = t.StateChanges.Max(c => (DateTimeOffset?)c.ChangedAt) ?? t.CreatedAt,
            })
            .ToListAsync(ct);
        return rows.ConvertAll(t => NotificationMapping.Ticket(t.Id, t.State, t.Title, t.EnteredAt));
    }

    /// <summary>
    /// Connection requests in the given state. <c>UpdatedAt</c> is the entry time: a request is edited only in Draft and
    /// InfoRequested, and every transition (submit, request info) sets it.
    /// </summary>
    private async Task<List<ConnectionRequestRow>> RequestRowsAsync(
        Expression<Func<ConnectionRequest, bool>> filter, bool forCustomer, CancellationToken ct)
    {
        var rows = await db.Set<ConnectionRequest>().AsNoTracking()
            .Where(filter)
            .OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id)
            .Take(MaxItems)
            .Select(r => new ConnectionRequestRow(r.Id, r.CustomerId, r.State, r.UpdatedAt, forCustomer))
            .ToListAsync(ct);
        return rows;
    }

    private async Task<Dictionary<Guid, string>> CustomerNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);
        return customers.ToDictionary(c => c.Id, c => c.DisplayName);
    }
}
