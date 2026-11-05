using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Tickets;

/// <param name="InstallationId">Installation of the anomaly.</param>
/// <param name="Title">Title of a new ticket.</param>
/// <param name="Description">Description of a new ticket, or the internal comment on an existing one.</param>
/// <param name="Priority">Priority of a new ticket.</param>
public sealed record AnomalyTicketRequest(Guid InstallationId, string Title, string Description, ServiceTicketPriority Priority);

/// <param name="TicketId">The new ticket, or the open one that was supplemented.</param>
/// <param name="Created">False when an open ticket of the installation got a comment instead.</param>
public sealed record AnomalyTicketResult(Guid TicketId, bool Created);

/// <summary>
/// Tickets from detected anomalies (#33). The methods only add to the shared <see cref="AppDbContext"/>; the caller saves,
/// so the ticket and the alarm that points to it are stored together or not at all.
/// </summary>
public interface IAnomalyTicketService
{
    /// <summary>
    /// Creates a ticket, unless the installation already has an open one (#116): then the anomaly is recorded on it as an
    /// internal comment. An open ticket from the detection is preferred, otherwise the newest open one.
    /// </summary>
    Task<AnomalyTicketResult> OpenOrSupplementAsync(AnomalyTicketRequest request, CancellationToken ct);

    /// <summary>Raises the priority of an open ticket and says why in an internal comment; anything else changes nothing.</summary>
    Task EscalateAsync(Guid ticketId, ServiceTicketPriority priority, string reason, CancellationToken ct);
}

internal sealed class AnomalyTicketService(AppDbContext db, TimeProvider time, ILogger<AnomalyTicketService> logger) : IAnomalyTicketService
{
    public async Task<AnomalyTicketResult> OpenOrSupplementAsync(AnomalyTicketRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var open = await db.Set<ServiceTicket>()
            .AsNoTracking()
            .Where(t => t.InstallationId == request.InstallationId && t.State != ServiceTicketState.Resolved && t.State != ServiceTicketState.Closed)
            .OrderByDescending(t => t.Origin == ServiceTicketOrigin.AnomalyDetected)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (open is { } ticketId)
        {
            db.Add(ServiceTicketComment.Create(ticketId, ServiceTicket.SystemActorId, request.Description, isPublic: false, now));
            logger.LogInformation(
                "Anomaly of installation {InstallationId} recorded on open ticket {TicketId} instead of a duplicate",
                request.InstallationId,
                ticketId);
            return new AnomalyTicketResult(ticketId, Created: false);
        }

        var customerId = await db.Set<Installation>()
            .AsNoTracking()
            .Where(i => i.Id == request.InstallationId)
            .Select(i => i.CustomerId)
            .FirstAsync(ct);
        var ticket = ServiceTicket.Create(
            request.InstallationId,
            customerId,
            request.Title,
            request.Description,
            request.Priority,
            ServiceTicketOrigin.AnomalyDetected,
            [],
            ServiceTicket.SystemActorId,
            now);
        db.Add(ticket);
        return new AnomalyTicketResult(ticket.Id, Created: true);
    }

    public async Task EscalateAsync(Guid ticketId, ServiceTicketPriority priority, string reason, CancellationToken ct)
    {
        var ticket = await db.Set<ServiceTicket>().FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        var now = time.GetUtcNow();
        if (ticket is not null && ticket.RaisePriority(priority, now))
        {
            db.Add(ServiceTicketComment.Create(ticketId, ServiceTicket.SystemActorId, reason, isPublic: false, now));
        }
    }
}
