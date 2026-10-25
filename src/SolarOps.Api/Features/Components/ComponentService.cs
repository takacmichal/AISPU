using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Components;

public interface IComponentService
{
    /// <summary>Lists components; <paramref name="active"/> null returns active and inactive ones.</summary>
    Task<IReadOnlyList<ComponentResponse>> ListAsync(ComponentType? type, bool? active, string? q, CancellationToken ct);

    Task<ComponentResponse> GetAsync(Guid id, CancellationToken ct);

    Task<ComponentResponse> CreateAsync(CreateComponentRequest request, CancellationToken ct);

    Task<ComponentResponse> UpdateAsync(Guid id, UpdateComponentRequest request, CancellationToken ct);

    Task<ComponentResponse> DeactivateAsync(Guid id, CancellationToken ct);

    Task<ComponentResponse> ActivateAsync(Guid id, CancellationToken ct);
}

public sealed class ComponentService(AppDbContext db, TimeProvider timeProvider) : IComponentService
{
    public async Task<IReadOnlyList<ComponentResponse>> ListAsync(ComponentType? type, bool? active, string? q, CancellationToken ct)
    {
        var query = db.Set<CatalogComponent>().AsNoTracking();
        if (type is { } t)
        {
            query = query.Where(c => c.Type == t);
        }

        if (active is { } a)
        {
            query = query.Where(c => c.IsActive == a);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Code, pattern, "\\")
                || EF.Functions.ILike(c.Name, pattern, "\\")
                || (c.Manufacturer != null && EF.Functions.ILike(c.Manufacturer, pattern, "\\")));
        }

        var components = await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(ct);
        return components.ConvertAll(ComponentResponse.FromEntity);
    }

    public async Task<ComponentResponse> GetAsync(Guid id, CancellationToken ct) =>
        ComponentResponse.FromEntity(await FindAsync(id, tracking: false, ct));

    public async Task<ComponentResponse> CreateAsync(CreateComponentRequest request, CancellationToken ct)
    {
        var details = request.ToDetails();
        await EnsureUniqueCodeAsync(null, details.Code, ct);

        var component = CatalogComponent.Create(details, timeProvider.GetUtcNow());
        db.Set<CatalogComponent>().Add(component);
        await SaveAsync(ct);
        return ComponentResponse.FromEntity(component);
    }

    public async Task<ComponentResponse> UpdateAsync(Guid id, UpdateComponentRequest request, CancellationToken ct)
    {
        var component = await FindAsync(id, tracking: true, ct);
        var details = request.ToDetails();
        await EnsureUniqueCodeAsync(id, details.Code, ct);

        component.Update(details, timeProvider.GetUtcNow());
        await SaveAsync(ct);
        return ComponentResponse.FromEntity(component);
    }

    public async Task<ComponentResponse> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var component = await FindAsync(id, tracking: true, ct);
        component.Deactivate(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return ComponentResponse.FromEntity(component);
    }

    public async Task<ComponentResponse> ActivateAsync(Guid id, CancellationToken ct)
    {
        var component = await FindAsync(id, tracking: true, ct);
        component.Activate(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return ComponentResponse.FromEntity(component);
    }

    private async Task<CatalogComponent> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Set<CatalogComponent>() : db.Set<CatalogComponent>().AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Component", id);
    }

    private async Task EnsureUniqueCodeAsync(Guid? id, string code, CancellationToken ct)
    {
        var normalized = CatalogComponent.NormalizeCode(code);
        if (await db.Set<CatalogComponent>().AnyAsync(c => c.Code == normalized && c.Id != id, ct))
        {
            throw new DomainException($"A component with code '{normalized}' already exists.");
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
            throw new DomainException("A component with the same code already exists.");
        }
    }
}
