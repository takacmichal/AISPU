using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Contracts;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Installations;

public interface IInstallationService
{
    Task<IReadOnlyList<InstallationResponse>> ListAsync(InstallationQuery query, CancellationToken ct);

    Task<InstallationDetailResponse> GetAsync(Guid id, CancellationToken ct);

    Task<InstallationDetailResponse> CreateAsync(CreateInstallationRequest request, CancellationToken ct);

    Task<InstallationDetailResponse> UpdateAsync(Guid id, UpdateInstallationRequest request, CancellationToken ct);

    Task<InstallationDetailResponse> ScheduleAsync(Guid id, ScheduleInstallationRequest request, CancellationToken ct);

    Task<InstallationDetailResponse> AssignTechniciansAsync(Guid id, AssignTechniciansRequest request, CancellationToken ct);

    Task<InstallationDetailResponse> TransitionAsync(Guid id, InstallationTransitionRequest request, CancellationToken ct);

    Task<IReadOnlyList<TechnicianResponse>> ListTechniciansAsync(CancellationToken ct);
}

internal sealed class InstallationService(AppDbContext db, ICurrentUser currentUser, ICustomerScope customerScope, TimeProvider time) : IInstallationService
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<InstallationResponse>> ListAsync(InstallationQuery query, CancellationToken ct)
    {
        var installations = db.Set<Installation>().AsNoTracking();

        var scope = await customerScope.GetAsync(ct);
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            installations = installations.Where(x => x.CustomerId == own);
        }

        if (query.State is { } state)
        {
            installations = installations.Where(x => x.State == state);
        }

        if (query.CustomerId is { } customerId)
        {
            installations = installations.Where(x => x.CustomerId == customerId);
        }

        if (query.ContractId is { } contractId)
        {
            installations = installations.Where(x => x.ContractId == contractId);
        }

        if (query.SupplyPointId is { } supplyPointId)
        {
            installations = installations.Where(x => x.SupplyPointId == supplyPointId);
        }

        if (query.MonitoringEnabled is { } monitored)
        {
            installations = installations.Where(x => x.MonitoringEnabled == monitored);
        }

        if (query.AssignedToMe)
        {
            var userId = currentUser.RequireUserId();
            installations = installations.Where(x => x.TechnicianIds.Contains(userId));
        }

        var result = await installations
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return result.Select(InstallationResponse.From).ToList();
    }

    public async Task<InstallationDetailResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = db.Set<Installation>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            visible = visible.Where(x => x.CustomerId == own);
        }

        // Another customer's installation looks exactly like a missing one.
        var installation = await visible
            .Include(x => x.StateChanges)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Installation", id);

        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDetailResponse> CreateAsync(CreateInstallationRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var contractId = request.ContractId!.Value;

        if (await db.Set<Installation>().AnyAsync(x => x.ContractId == contractId, ct))
        {
            throw new DomainException($"Contract '{contractId}' already has an installation.");
        }

        var installation = Installation.Create(
            request.CustomerId!.Value,
            contractId,
            request.SupplyPointId!.Value,
            request.Name!,
            request.PlannedPowerKwp,
            request.Note,
            userId,
            time.GetUtcNow());

        db.Add(installation);
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDetailResponse> UpdateAsync(Guid id, UpdateInstallationRequest request, CancellationToken ct)
    {
        var installation = await LoadAsync(id, ct);
        installation.UpdateDetails(request.Name!, request.PlannedPowerKwp, request.Note, time.GetUtcNow());
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDetailResponse> ScheduleAsync(Guid id, ScheduleInstallationRequest request, CancellationToken ct)
    {
        var installation = await LoadAsync(id, ct);
        var start = request.ScheduledStart!.Value;
        var end = request.ScheduledEnd!.Value;
        var now = time.GetUtcNow();

        // #88: no date in the past. An unchanged start may be past already (extending an ongoing mounting).
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (start < today && start != installation.ScheduledStart)
        {
            throw new RequestValidationException(nameof(ScheduleInstallationRequest.ScheduledStart), "The installation date must not be in the past.");
        }

        if (end < today)
        {
            throw new RequestValidationException(nameof(ScheduleInstallationRequest.ScheduledEnd), "The installation date must not be in the past.");
        }

        if (!await ConnectionApproval.IsApprovedAsync(db, id, ct))
        {
            throw new DomainException("The installation can be scheduled only after the connection request is approved by the distributor.");
        }

        installation.Schedule(start, end, now);
        await EnsureNoCollisionAsync(installation, ct);
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDetailResponse> AssignTechniciansAsync(Guid id, AssignTechniciansRequest request, CancellationToken ct)
    {
        var installation = await LoadAsync(id, ct);
        var requested = request.TechnicianIds!.Distinct().ToList();

        if (requested.Count > 0)
        {
            var technicians = await (
                    from userRole in db.Set<IdentityUserRole<Guid>>()
                    join role in db.Set<IdentityRole<Guid>>() on userRole.RoleId equals role.Id
                    where role.Name == Roles.Technician && requested.Contains(userRole.UserId)
                    select userRole.UserId)
                .Distinct()
                .ToListAsync(ct);

            var unknown = requested.Except(technicians).ToList();
            if (unknown.Count > 0)
            {
                throw new RequestValidationException(
                    nameof(AssignTechniciansRequest.TechnicianIds),
                    $"Users are not technicians: {string.Join(", ", unknown)}.");
            }
        }

        installation.AssignTechnicians(requested, time.GetUtcNow());
        await EnsureNoCollisionAsync(installation, ct);
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDetailResponse> TransitionAsync(Guid id, InstallationTransitionRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installation = await LoadAsync(id, ct);
        if (request.TargetState == InstallationState.ComponentsOrdered)
        {
            await EnsureDepositPaidAsync(installation.ContractId, ct);
        }

        installation.TransitionTo(request.TargetState!.Value, userId, request.Note, time.GetUtcNow());
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<IReadOnlyList<TechnicianResponse>> ListTechniciansAsync(CancellationToken ct) =>
        await (
                from userRole in db.Set<IdentityUserRole<Guid>>()
                join role in db.Set<IdentityRole<Guid>>() on userRole.RoleId equals role.Id
                join user in db.Set<AppUser>() on userRole.UserId equals user.Id
                where role.Name == Roles.Technician
                orderby user.Email, user.Id
                select new TechnicianResponse(user.Id, user.UserName, user.Email))
            .AsNoTracking()
            .ToListAsync(ct);

    // #89: a technician must not be planned on two installations at once. Commissioned and handed-over installations
    // no longer need the team, so they do not block the window.
    private async Task EnsureNoCollisionAsync(Installation installation, CancellationToken ct)
    {
        if (installation.ScheduledStart is not { } start || installation.ScheduledEnd is not { } end || installation.TechnicianIds.Count == 0)
        {
            return;
        }

        var technicians = installation.TechnicianIds;
        var collisions = await db.Set<Installation>().AsNoTracking()
            .Where(x => x.Id != installation.Id
                        && x.State != InstallationState.Commissioned
                        && x.State != InstallationState.HandedOver
                        && x.ScheduledStart != null && x.ScheduledEnd != null
                        && x.ScheduledStart <= end && start <= x.ScheduledEnd
                        && x.TechnicianIds.Any(t => technicians.Contains(t)))
            .OrderBy(x => x.ScheduledStart)
            .Select(x => new { x.Name, x.ScheduledStart, x.ScheduledEnd, x.TechnicianIds })
            .ToListAsync(ct);

        if (collisions.Count > 0)
        {
            var details = collisions.Select(c =>
                $"'{c.Name}' {c.ScheduledStart:yyyy-MM-dd}..{c.ScheduledEnd:yyyy-MM-dd} (technicians {string.Join(", ", c.TechnicianIds.Intersect(technicians))})");
            throw new DomainException($"Technician schedule collision with: {string.Join("; ", details)}.");
        }
    }

    // Realization starts with ordering components. A contract that does not exist (installation created by hand) is not checked.
    private async Task EnsureDepositPaidAsync(Guid contractId, CancellationToken ct)
    {
        var deposit = await db.Set<Contract>().AsNoTracking()
            .Where(c => c.Id == contractId)
            .Select(c => (DepositStatus?)c.DepositStatus)
            .FirstOrDefaultAsync(ct);
        if (deposit is { } status && status != DepositStatus.Paid)
        {
            throw new DomainException("Realization cannot start before the deposit of the contract is paid.");
        }
    }

    private async Task<Installation> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<Installation>()
            .Include(x => x.StateChanges)
            .Include(x => x.ChecklistItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundException("Installation", id);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Race on the unique contract index (the pre-check above handles the common case).
            throw new DomainException("The contract already has an installation.");
        }
    }
}
