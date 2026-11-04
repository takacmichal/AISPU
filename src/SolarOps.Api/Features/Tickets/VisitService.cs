using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Tickets;

public interface IVisitService
{
    Task<TicketAssignmentResponse> AssignTechnicianAsync(Guid ticketId, AssignTechnicianRequest request, CancellationToken ct);

    Task<VisitResponse> PlanAsync(Guid ticketId, PlanVisitRequest request, CancellationToken ct);

    Task<TicketVisitsResponse> ListForTicketAsync(Guid ticketId, CancellationToken ct);

    Task<VisitResponse> GetAsync(Guid visitId, CancellationToken ct);

    Task<VisitResponse> RescheduleAsync(Guid visitId, RescheduleVisitRequest request, CancellationToken ct);

    Task<VisitResponse> StartAsync(Guid visitId, CancellationToken ct);

    Task<VisitResponse> ReportAsync(Guid visitId, VisitReportRequest request, CancellationToken ct);

    Task<VisitResponse> ReplaceMaterialsAsync(Guid visitId, ReplaceVisitMaterialsRequest request, CancellationToken ct);

    Task<VisitDeviceReplacementResult> ReplaceDeviceAsync(Guid visitId, VisitDeviceReplacementRequest request, CancellationToken ct);

    Task<TicketStateResponse> VerifyAsync(Guid ticketId, CancellationToken ct);

    Task<TicketStateResponse> CloseAsync(Guid ticketId, CloseTicketRequest request, CancellationToken ct);

    Task<TicketConfirmationResponse> ConfirmAsync(Guid ticketId, ConfirmTicketRequest request, CancellationToken ct);

    Task<TicketConfirmationResponse> ReopenAsync(Guid ticketId, ReopenTicketRequest request, CancellationToken ct);

    /// <summary>Visits of a technician that start in [from, to), ordered by time.</summary>
    Task<TechnicianCalendarResponse> CalendarAsync(Guid technicianId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);

    Task<TechnicianCalendarResponse> MyCalendarAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

internal sealed class VisitService(
    AppDbContext db,
    ICurrentUser currentUser,
    ICustomerScope customerScope,
    IDeviceService devices,
    TimeProvider time) : IVisitService
{
    public const int MaxCalendarDays = 92;

    public async Task<TicketAssignmentResponse> AssignTechnicianAsync(Guid ticketId, AssignTechnicianRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var technicianId = request.TechnicianId!.Value;
        var ticket = await LoadTicketAsync(ticketId, ct);

        if (ticket.State is not (ServiceTicketState.Triaged or ServiceTicketState.RemoteDiagnosis or ServiceTicketState.VisitScheduled))
        {
            throw new DomainException($"A technician can be assigned to a ticket in Triaged, RemoteDiagnosis or VisitScheduled, not {ticket.State}.");
        }

        var isTechnician = await (
                from userRole in db.Set<IdentityUserRole<Guid>>()
                join role in db.Set<IdentityRole<Guid>>() on userRole.RoleId equals role.Id
                where role.Name == Roles.Technician && userRole.UserId == technicianId
                select userRole.UserId)
            .AnyAsync(ct);
        if (!isTechnician)
        {
            throw new RequestValidationException(nameof(AssignTechnicianRequest.TechnicianId), $"User '{technicianId}' is not a technician.");
        }

        var assignment = await db.Set<ServiceTicketAssignment>().FirstOrDefaultAsync(x => x.ServiceTicketId == ticketId, ct);
        if (assignment is not null && assignment.TechnicianId != technicianId
            && await db.Set<ServiceVisit>().AnyAsync(v => v.ServiceTicketId == ticketId
                && (v.State == ServiceVisitState.Planned || v.State == ServiceVisitState.InProgress), ct))
        {
            throw new DomainException("The ticket has an active visit; the technician cannot be changed until it is done.");
        }

        var now = time.GetUtcNow();
        if (assignment is null)
        {
            assignment = ServiceTicketAssignment.Create(ticketId, technicianId, userId, now);
            db.Add(assignment);
        }
        else
        {
            assignment.Reassign(technicianId, userId, now);
        }

        await SaveAsync(ct);
        return TicketAssignmentResponse.From(assignment);
    }

    public async Task<VisitResponse> PlanAsync(Guid ticketId, PlanVisitRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var ticket = await LoadTicketAsync(ticketId, ct);
        var assignment = await db.Set<ServiceTicketAssignment>().AsNoTracking().FirstOrDefaultAsync(x => x.ServiceTicketId == ticketId, ct)
            ?? throw new DomainException("Assign a technician to the ticket before planning a visit.");

        var now = time.GetUtcNow();
        var visit = ServiceVisit.Plan(ticketId, assignment.TechnicianId, request.Start!.Value, request.EstimatedMinutes!.Value, userId, now);
        ticket.ScheduleVisit(userId, now);
        await EnsureNoCollisionAsync(visit.TechnicianId, visit.PlannedStart, visit.PlannedEnd, excludeVisitId: null, ct);

        db.Add(visit);
        await SaveAsync(ct);
        return (await BuildAsync([visit], restricted: false, ct))[0];
    }

    public async Task<TicketVisitsResponse> ListForTicketAsync(Guid ticketId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = VisibleTickets(scope);
        if (!await visible.AnyAsync(x => x.Id == ticketId, ct))
        {
            throw new NotFoundException("Service ticket", ticketId);
        }

        var visits = await db.Set<ServiceVisit>().AsNoTracking()
            .Where(v => v.ServiceTicketId == ticketId)
            .Include(v => v.Reschedules)
            .OrderBy(v => v.PlannedStart).ThenBy(v => v.Id)
            .ToListAsync(ct);
        var technicianId = await db.Set<ServiceTicketAssignment>().AsNoTracking()
            .Where(x => x.ServiceTicketId == ticketId).Select(x => (Guid?)x.TechnicianId).FirstOrDefaultAsync(ct);

        return new TicketVisitsResponse(ticketId, scope.IsRestricted ? null : technicianId, await BuildAsync(visits, scope.IsRestricted, ct));
    }

    public async Task<VisitResponse> GetAsync(Guid visitId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visit = await db.Set<ServiceVisit>().AsNoTracking().Include(v => v.Reschedules)
            .Where(v => v.Id == visitId && VisibleTickets(scope).Any(t => t.Id == v.ServiceTicketId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Service visit", visitId);
        return (await BuildAsync([visit], scope.IsRestricted, ct))[0];
    }

    public async Task<VisitResponse> RescheduleAsync(Guid visitId, RescheduleVisitRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var visit = await LoadVisitAsync(visitId, ct);

        var now = time.GetUtcNow();
        visit.Reschedule(request.Start!.Value, request.EstimatedMinutes!.Value, request.Reason, userId, now);
        await EnsureNoCollisionAsync(visit.TechnicianId, visit.PlannedStart, visit.PlannedEnd, visit.Id, ct);

        await SaveAsync(ct);
        return (await BuildAsync([visit], restricted: false, ct))[0];
    }

    public async Task<VisitResponse> StartAsync(Guid visitId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var visit = await LoadVisitAsync(visitId, ct);
        EnsureMayAct(visit.TechnicianId, visitId);
        var ticket = await LoadTicketAsync(visit.ServiceTicketId, ct);

        var now = time.GetUtcNow();
        visit.Start(userId, now);
        ticket.StartVisit(userId, now);

        await SaveAsync(ct);
        return (await BuildAsync([visit], restricted: false, ct))[0];
    }

    public async Task<VisitResponse> ReportAsync(Guid visitId, VisitReportRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var visit = await LoadVisitAsync(visitId, ct);
        EnsureMayAct(visit.TechnicianId, visitId);

        visit.Report(
            request.Cause!.Value, request.Findings, request.WorkDone, request.ArrivedAt!.Value, request.DepartedAt!.Value, userId, time.GetUtcNow());

        await SaveAsync(ct);
        return (await BuildAsync([visit], restricted: false, ct))[0];
    }

    public async Task<VisitResponse> ReplaceMaterialsAsync(Guid visitId, ReplaceVisitMaterialsRequest request, CancellationToken ct)
    {
        var visit = await LoadVisitAsync(visitId, ct);
        EnsureMayAct(visit.TechnicianId, visitId);
        await EnsureProtocolOpenAsync(visit, ct);

        var materials = request.Materials!
            .Select(m => ServiceVisitMaterial.Create(visit.ServiceTicketId, visit.Id, m.Name, m.Quantity!.Value, m.Unit))
            .ToList();
        var existing = await db.Set<ServiceVisitMaterial>().Where(m => m.ServiceVisitId == visitId).ToListAsync(ct);
        db.RemoveRange(existing);
        db.AddRange(materials);

        await SaveAsync(ct);
        return (await BuildAsync([visit], restricted: false, ct))[0];
    }

    public async Task<VisitDeviceReplacementResult> ReplaceDeviceAsync(Guid visitId, VisitDeviceReplacementRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var visit = await LoadVisitAsync(visitId, ct);
        EnsureMayAct(visit.TechnicianId, visitId);
        var ticket = await EnsureProtocolOpenAsync(visit, ct);

        // The replacement is the existing flow of the Installations module (serial validation, history of the old
        // device); the link row is written in the same transaction so neither exists without the other.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var replacement = await devices.ReplaceAsync(ticket.InstallationId, request.DeviceId!.Value, request.Replacement!, ct);
        var link = ServiceVisitDeviceReplacement.Create(
            ticket.Id, visit.Id, replacement.Replaced.Id, replacement.Replacement.Id, request.Replacement!.Reason!, userId, time.GetUtcNow());
        db.Add(link);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new VisitDeviceReplacementResult(ToResponse(link), replacement.Replaced, replacement.Replacement);
    }

    public async Task<TicketStateResponse> VerifyAsync(Guid ticketId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var ticket = await LoadTicketAsync(ticketId, ct);
        var assigned = await db.Set<ServiceTicketAssignment>().AsNoTracking()
            .Where(x => x.ServiceTicketId == ticketId).Select(x => (Guid?)x.TechnicianId).FirstOrDefaultAsync(ct);
        EnsureMayAct(assigned, ticketId);

        if (!await db.Set<ServiceVisit>().AnyAsync(v => v.ServiceTicketId == ticketId && v.State == ServiceVisitState.Done, ct))
        {
            throw new DomainException("Production can be verified only after the service protocol of a visit is written.");
        }

        ticket.Verify(userId, time.GetUtcNow());
        await SaveAsync(ct);
        return new TicketStateResponse(ticket.Id, ticket.State, ticket.UpdatedAt);
    }

    public async Task<TicketStateResponse> CloseAsync(Guid ticketId, CloseTicketRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var ticket = await LoadTicketAsync(ticketId, ct);

        // A ticket that went through a visit needs the protocol (story #129); the remote path has none (BPMN #67).
        var states = await db.Set<ServiceVisit>().AsNoTracking().Where(v => v.ServiceTicketId == ticketId)
            .Select(v => v.State).ToListAsync(ct);
        var hasVisit = states.Any(s => s != ServiceVisitState.Cancelled);
        if (hasVisit && !states.Contains(ServiceVisitState.Done))
        {
            throw new DomainException("The ticket cannot be closed before the service protocol of its visit is written.");
        }

        ticket.Close(userId, request.Note, time.GetUtcNow());
        await SaveAsync(ct);
        return new TicketStateResponse(ticket.Id, ticket.State, ticket.UpdatedAt);
    }

    public async Task<TicketConfirmationResponse> ConfirmAsync(Guid ticketId, ConfirmTicketRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var ticket = await LoadVisibleWithHistoryAsync(ticketId, ct);
        if (ticket.State != ServiceTicketState.Closed)
        {
            throw new DomainException($"Only a closed service ticket can be confirmed, this one is {ticket.State}.");
        }

        var closedAt = ticket.ClosedAt!.Value;
        if (await db.Set<ServiceTicketConfirmation>().AnyAsync(
                c => c.ServiceTicketId == ticketId && c.Decision == TicketConfirmationDecision.Confirmed && c.DecidedAt >= closedAt, ct))
        {
            throw new DomainException("The ticket has already been confirmed.");
        }

        var confirmation = ServiceTicketConfirmation.Create(ticketId, TicketConfirmationDecision.Confirmed, request.Comment, userId, time.GetUtcNow());
        db.Add(confirmation);
        await SaveAsync(ct);
        return ToResponse(confirmation, ticket.State);
    }

    public async Task<TicketConfirmationResponse> ReopenAsync(Guid ticketId, ReopenTicketRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new RequestValidationException(nameof(ReopenTicketRequest.Reason), "The Reason field is required.");
        }

        var ticket = await LoadVisibleWithHistoryAsync(ticketId, ct);

        var now = time.GetUtcNow();
        ticket.Reopen(userId, request.Reason, now);
        var confirmation = ServiceTicketConfirmation.Create(ticketId, TicketConfirmationDecision.Reopened, request.Reason, userId, now);
        db.Add(confirmation);
        await SaveAsync(ct);
        return ToResponse(confirmation, ticket.State);
    }

    public Task<TechnicianCalendarResponse> MyCalendarAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        CalendarAsync(currentUser.RequireUserId(), from, to, ct);

    public async Task<TechnicianCalendarResponse> CalendarAsync(Guid technicianId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (from is null)
        {
            errors["from"] = ["The from field is required."];
        }

        if (to is null)
        {
            errors["to"] = ["The to field is required."];
        }

        if (from is { } rangeFrom && to is { } rangeTo)
        {
            if (rangeTo <= rangeFrom)
            {
                errors["to"] = ["The to field must be after from."];
            }
            else if (rangeTo - rangeFrom > TimeSpan.FromDays(MaxCalendarDays))
            {
                errors["to"] = [$"The range is at most {MaxCalendarDays} days."];
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var start = from!.Value.ToUniversalTime();
        var end = to!.Value.ToUniversalTime();
        var rows = await (
                from v in db.Set<ServiceVisit>().AsNoTracking()
                join t in db.Set<ServiceTicket>().AsNoTracking() on v.ServiceTicketId equals t.Id
                where v.TechnicianId == technicianId && v.State != ServiceVisitState.Cancelled && v.PlannedStart >= start && v.PlannedStart < end
                orderby v.PlannedStart, v.Id
                select new { Visit = v, t.Title, t.Priority, t.State, t.InstallationId, t.CustomerId })
            .ToListAsync(ct);

        var installationIds = rows.Select(r => r.InstallationId).Distinct().ToList();
        var installations = await db.Set<Installation>().AsNoTracking().Where(i => installationIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Name, i.SupplyPointId }).ToDictionaryAsync(i => i.Id, ct);
        var supplyPointIds = installations.Values.Select(i => i.SupplyPointId).Distinct().ToList();
        var addresses = await db.Set<SupplyPoint>().AsNoTracking().Where(s => supplyPointIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Address, ct);
        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var items = rows.ConvertAll(r =>
        {
            installations.TryGetValue(r.InstallationId, out var installation);
            PostalAddress? address = null;
            if (installation is not null && addresses.TryGetValue(installation.SupplyPointId, out var found))
            {
                address = found;
            }

            CalendarCustomer? customer = customers.TryGetValue(r.CustomerId, out var c)
                ? new CalendarCustomer(c.Id, c.DisplayName, c.Phone, c.Email)
                : null;
            return new CalendarVisit(
                r.Visit.Id, r.Visit.ServiceTicketId, r.Title, r.Priority, r.State, r.Visit.State, r.Visit.PlannedStart, r.Visit.PlannedEnd,
                r.Visit.EstimatedMinutes, r.InstallationId, installation?.Name, address, customer);
        });

        return new TechnicianCalendarResponse(technicianId, start, end, items);
    }

    /// <summary>
    /// A technician acts only on tickets and visits assigned to them; someone else's look exactly like missing ones.
    /// Admin and Operator (the back office) act on any.
    /// </summary>
    private void EnsureMayAct(Guid? assignedTechnicianId, Guid resourceId)
    {
        if (currentUser.IsInRole(Roles.Admin) || currentUser.IsInRole(Roles.Operator))
        {
            return;
        }

        if (assignedTechnicianId != currentUser.RequireUserId())
        {
            throw new NotFoundException("Service ticket or visit", resourceId);
        }
    }

    /// <summary>The protocol is closed once the ticket moved past OnSite (verified, resolved, closed) or the visit is not on site yet.</summary>
    private async Task<ServiceTicket> EnsureProtocolOpenAsync(ServiceVisit visit, CancellationToken ct)
    {
        var ticket = await LoadTicketAsync(visit.ServiceTicketId, ct);
        if (visit.State is not (ServiceVisitState.InProgress or ServiceVisitState.Done) || ticket.State != ServiceTicketState.OnSite)
        {
            throw new DomainException("Material and replaced devices can be recorded only while the technician is on site, before the ticket is verified.");
        }

        return ticket;
    }

    private async Task EnsureNoCollisionAsync(Guid technicianId, DateTimeOffset start, DateTimeOffset end, Guid? excludeVisitId, CancellationToken ct)
    {
        var clash = await db.Set<ServiceVisit>().AsNoTracking()
            .Where(v => v.TechnicianId == technicianId
                && (v.State == ServiceVisitState.Planned || v.State == ServiceVisitState.InProgress)
                && v.PlannedStart < end && start < v.PlannedEnd
                && (excludeVisitId == null || v.Id != excludeVisitId))
            .OrderBy(v => v.PlannedStart)
            .Select(v => new { v.PlannedStart, v.PlannedEnd })
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
        {
            throw new DomainException(
                $"The technician already has a visit from {clash.PlannedStart:O} to {clash.PlannedEnd:O}.");
        }
    }

    private async Task<ServiceVisit> LoadVisitAsync(Guid visitId, CancellationToken ct)
    {
        // Visits are handled by staff and technicians only; a customer never reaches these methods (policy).
        var scope = await customerScope.GetAsync(ct);
        return await db.Set<ServiceVisit>().Include(v => v.Reschedules)
            .Where(v => v.Id == visitId && VisibleTickets(scope).Any(t => t.Id == v.ServiceTicketId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Service visit", visitId);
    }

    private async Task<ServiceTicket> LoadTicketAsync(Guid ticketId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        return await VisibleTickets(scope).FirstOrDefaultAsync(x => x.Id == ticketId, ct)
            ?? throw new NotFoundException("Service ticket", ticketId);
    }

    private async Task<ServiceTicket> LoadVisibleWithHistoryAsync(Guid ticketId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        return await VisibleTickets(scope).Include(x => x.StateChanges).FirstOrDefaultAsync(x => x.Id == ticketId, ct)
            ?? throw new NotFoundException("Service ticket", ticketId);
    }

    private IQueryable<ServiceTicket> VisibleTickets(DataScope scope)
    {
        var query = db.Set<ServiceTicket>();
        return scope.IsRestricted ? query.Where(x => x.CustomerId == scope.OwnCustomerId) : query;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("The service ticket was changed by another request. Reload it and try again.");
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException("The technician was assigned by another request. Reload and try again.");
        }
    }

    private async Task<List<VisitResponse>> BuildAsync(IReadOnlyList<ServiceVisit> visits, bool restricted, CancellationToken ct)
    {
        var ids = visits.Select(v => v.Id).ToList();
        var materials = await db.Set<ServiceVisitMaterial>().AsNoTracking().Where(m => ids.Contains(m.ServiceVisitId))
            .OrderBy(m => m.Id).ToListAsync(ct);
        var replacements = await db.Set<ServiceVisitDeviceReplacement>().AsNoTracking().Where(r => ids.Contains(r.ServiceVisitId))
            .OrderBy(r => r.ReplacedAt).ThenBy(r => r.Id).ToListAsync(ct);

        return visits.Select(v => new VisitResponse(
            v.Id,
            v.ServiceTicketId,
            restricted ? null : v.TechnicianId,
            v.State,
            v.PlannedStart,
            v.PlannedEnd,
            v.EstimatedMinutes,
            v.StartedAt,
            v.State == ServiceVisitState.Done
                ? new VisitReportResponse(
                    v.Cause!.Value, restricted ? null : v.Findings, v.WorkDone!, v.ArrivedAt!.Value, v.DepartedAt!.Value, v.ReportedBy!.Value, v.ReportedAt!.Value)
                : null,
            v.Reschedules.OrderBy(r => r.ChangedAt).ThenBy(r => r.Id)
                .Select(r => new VisitRescheduleResponse(r.PreviousStart, r.PreviousEnd, r.Reason, r.ChangedBy, r.ChangedAt)).ToList(),
            materials.Where(m => m.ServiceVisitId == v.Id).Select(m => new VisitMaterialResponse(m.Id, m.Name, m.Quantity, m.Unit)).ToList(),
            replacements.Where(r => r.ServiceVisitId == v.Id).Select(ToResponse).ToList())).ToList();
    }

    private static VisitDeviceReplacementResponse ToResponse(ServiceVisitDeviceReplacement r) =>
        new(r.Id, r.OldDeviceId, r.NewDeviceId, r.Reason, r.ReplacedBy, r.ReplacedAt);

    private static TicketConfirmationResponse ToResponse(ServiceTicketConfirmation c, ServiceTicketState state) =>
        new(c.ServiceTicketId, c.Decision, c.Comment, c.DecidedAt, state);
}
