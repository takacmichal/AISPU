using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.ConnectionRequests;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.SupplyPoints;

public interface ISupplyPointService
{
    Task<IReadOnlyList<SupplyPointResponse>> ListAsync(Guid customerId, CancellationToken ct);

    Task<SupplyPointResponse> GetAsync(Guid customerId, Guid id, CancellationToken ct);

    Task<SupplyPointResponse> CreateAsync(Guid customerId, CreateSupplyPointRequest request, CancellationToken ct);

    Task<SupplyPointResponse> UpdateAsync(Guid customerId, Guid id, UpdateSupplyPointRequest request, CancellationToken ct);

    Task DeleteAsync(Guid customerId, Guid id, CancellationToken ct);
}

public sealed class SupplyPointService(AppDbContext db, TimeProvider timeProvider) : ISupplyPointService
{
    public async Task<IReadOnlyList<SupplyPointResponse>> ListAsync(Guid customerId, CancellationToken ct)
    {
        await EnsureCustomerAsync(customerId, ct);
        var items = await db.Set<SupplyPoint>().AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return items.ConvertAll(SupplyPointResponse.FromEntity);
    }

    public async Task<SupplyPointResponse> GetAsync(Guid customerId, Guid id, CancellationToken ct) =>
        SupplyPointResponse.FromEntity(await FindAsync(customerId, id, tracking: false, ct));

    public async Task<SupplyPointResponse> CreateAsync(Guid customerId, CreateSupplyPointRequest request, CancellationToken ct)
    {
        await EnsureCustomerAsync(customerId, ct);
        var eic = SupplyPoint.NormalizeEic(request.EicCode)!;
        await EnsureEicUniqueAsync(eic, null, ct);

        var supplyPoint = SupplyPoint.Create(
            customerId, CleanName(request.Name), eic, request.Address!.ToDomain(), request.MainBreakerAmps!.Value, timeProvider.GetUtcNow());
        db.Set<SupplyPoint>().Add(supplyPoint);
        await SaveAsync(ct);
        return SupplyPointResponse.FromEntity(supplyPoint);
    }

    public async Task<SupplyPointResponse> UpdateAsync(Guid customerId, Guid id, UpdateSupplyPointRequest request, CancellationToken ct)
    {
        var supplyPoint = await FindAsync(customerId, id, tracking: true, ct);
        var eic = SupplyPoint.NormalizeEic(request.EicCode)!;
        await EnsureEicUniqueAsync(eic, id, ct);

        supplyPoint.Update(CleanName(request.Name), eic, request.Address!.ToDomain(), request.MainBreakerAmps!.Value, timeProvider.GetUtcNow());
        await SaveAsync(ct);
        return SupplyPointResponse.FromEntity(supplyPoint);
    }

    public async Task DeleteAsync(Guid customerId, Guid id, CancellationToken ct)
    {
        var supplyPoint = await FindAsync(customerId, id, tracking: true, ct);

        if (await db.Set<Installation>().AnyAsync(x => x.SupplyPointId == id, ct))
        {
            throw new DomainException("The supply point has an installation and cannot be deleted.");
        }

        if (await db.Set<FveDesign>().AnyAsync(x => x.SupplyPointId == id, ct)
            || await db.Set<ConnectionRequest>().AnyAsync(x => x.SupplyPointId == id, ct))
        {
            throw new DomainException("The supply point is used by an FVE design or a connection request and cannot be deleted.");
        }

        db.Set<SupplyPoint>().Remove(supplyPoint);
        await db.SaveChangesAsync(ct);
    }

    private static string? CleanName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken ct)
    {
        if (!await db.Set<Customer>().AnyAsync(c => c.Id == customerId, ct))
        {
            throw new NotFoundException("Customer", customerId);
        }
    }

    private async Task<SupplyPoint> FindAsync(Guid customerId, Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Set<SupplyPoint>() : db.Set<SupplyPoint>().AsNoTracking();
        return await query.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customerId, ct)
            ?? throw new NotFoundException("SupplyPoint", id);
    }

    private async Task EnsureEicUniqueAsync(string eic, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Set<SupplyPoint>().AnyAsync(x => x.EicCode == eic && x.Id != exceptId, ct))
        {
            throw new DomainException($"A supply point with EIC '{eic}' already exists.");
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request won the race after our pre-check.
            throw new DomainException("A supply point with the same EIC already exists.");
        }
    }
}
