using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
}

internal sealed class InstallationService(AppDbContext db, ICurrentUser currentUser, TimeProvider time) : IInstallationService
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<InstallationResponse>> ListAsync(InstallationQuery query, CancellationToken ct)
    {
        var installations = db.Set<Installation>().AsNoTracking();

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
        var installation = await db.Set<Installation>()
            .AsNoTracking()
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
        installation.Schedule(request.ScheduledStart!.Value, request.ScheduledEnd!.Value, time.GetUtcNow());
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
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    public async Task<InstallationDetailResponse> TransitionAsync(Guid id, InstallationTransitionRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var installation = await LoadAsync(id, ct);
        installation.TransitionTo(request.TargetState!.Value, userId, request.Note, time.GetUtcNow());
        await SaveAsync(ct);
        return InstallationDetailResponse.From(installation);
    }

    private async Task<Installation> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<Installation>()
            .Include(x => x.StateChanges)
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
