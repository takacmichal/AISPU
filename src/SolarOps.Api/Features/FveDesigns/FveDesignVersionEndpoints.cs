using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.FveDesigns;

internal static class FveDesignVersionEndpoints
{
    internal static async Task<Ok<DesignConfigurationPreviewResponse>> Preview(
        Guid id,
        ConfigureDesignRequest request,
        AppDbContext db,
        IYieldEstimator estimator,
        CancellationToken ct)
    {
        var design = await FveDesignEndpoints.LoadAsync(db, id, ct);
        var (panel, inverter) = await LoadComponentsAsync(db, request, ct);
        var panelCount = request.PanelCount!.Value;

        var compatibility = FveDesignVersion.Evaluate(design, panel, panelCount, inverter);
        YieldEstimateResponse? estimate = null;
        if (compatibility.IsCompatible)
        {
            estimate = YieldEstimateResponse.From(await EstimateAsync(estimator, design, compatibility, ct));
        }

        return TypedResults.Ok(new DesignConfigurationPreviewResponse(CompatibilityResponse.From(compatibility), estimate));
    }

    internal static async Task<Created<FveDesignVersionResponse>> Save(
        Guid id,
        ConfigureDesignRequest request,
        AppDbContext db,
        IYieldEstimator estimator,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken ct)
    {
        var design = await FveDesignEndpoints.LoadAsync(db, id, ct);
        var (panel, inverter) = await LoadComponentsAsync(db, request, ct);
        var panelCount = request.PanelCount!.Value;

        var compatibility = FveDesignVersion.Evaluate(design, panel, panelCount, inverter);
        if (!compatibility.IsCompatible)
        {
            throw new RequestValidationException("PanelCount", string.Join(" ", compatibility.Errors));
        }

        var estimate = await EstimateAsync(estimator, design, compatibility, ct);
        var lastNumber = await db.Set<FveDesignVersion>()
            .Where(v => v.DesignId == id)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var version = FveDesignVersion.Create(
            design, panel, panelCount, inverter, lastNumber + 1, estimate, currentUser.RequireUserId(), time.GetUtcNow());
        db.Add(version);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(FveDesignVersionConfiguration.VersionNumberIndex))
        {
            throw new DomainException("Another version of this design was saved at the same time; try again.");
        }

        return TypedResults.Created($"/api/designs/{id}/versions/{version.VersionNumber}", FveDesignVersionResponse.From(version));
    }

    internal static async Task<Ok<List<FveDesignVersionResponse>>> List(Guid id, AppDbContext db, CancellationToken ct)
    {
        await EnsureDesignExistsAsync(db, id, ct);
        var versions = await db.Set<FveDesignVersion>().AsNoTracking()
            .Where(v => v.DesignId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
        return TypedResults.Ok(versions.Select(FveDesignVersionResponse.From).ToList());
    }

    internal static async Task<Ok<FveDesignVersionResponse>> Get(Guid id, int number, AppDbContext db, CancellationToken ct)
    {
        var version = await db.Set<FveDesignVersion>().AsNoTracking()
            .FirstOrDefaultAsync(v => v.DesignId == id && v.VersionNumber == number, ct)
            ?? throw new NotFoundException($"Version {number} of design '{id}' was not found.");
        return TypedResults.Ok(FveDesignVersionResponse.From(version));
    }

    private static async Task EnsureDesignExistsAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        if (!await db.Set<FveDesign>().AnyAsync(d => d.Id == id, ct))
        {
            throw new NotFoundException("Design", id);
        }
    }

    private static async Task<(CatalogComponent Panel, CatalogComponent Inverter)> LoadComponentsAsync(
        AppDbContext db, ConfigureDesignRequest request, CancellationToken ct)
    {
        var panelId = request.PanelId!.Value;
        var inverterId = request.InverterId!.Value;
        var components = await db.Set<CatalogComponent>().AsNoTracking()
            .Where(c => c.Id == panelId || c.Id == inverterId)
            .ToListAsync(ct);

        var panel = components.FirstOrDefault(c => c.Id == panelId) ?? throw new NotFoundException("Component", panelId);
        var inverter = components.FirstOrDefault(c => c.Id == inverterId) ?? throw new NotFoundException("Component", inverterId);
        return (panel, inverter);
    }

    private static Task<YieldEstimate> EstimateAsync(
        IYieldEstimator estimator, FveDesign design, CompatibilityResult compatibility, CancellationToken ct) =>
        estimator.EstimateAsync(
            new YieldEstimateInput(
                design.Latitude!.Value,
                design.Longitude!.Value,
                compatibility.DcPowerKwp,
                compatibility.AcPowerKw,
                design.RoofPlanes),
            ct);
}
