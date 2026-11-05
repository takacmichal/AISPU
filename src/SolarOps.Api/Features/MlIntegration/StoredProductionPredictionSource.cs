using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Production;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>Predictions for the production comparison (#101), read from the database; the ML service is not called.</summary>
internal sealed class StoredProductionPredictionSource(AppDbContext db) : IProductionPredictionSource
{
    public async Task<ProductionPrediction> GetAsync(Guid installationId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.Set<StoredPrediction>()
            .AsNoTracking()
            .Where(p => p.InstallationId == installationId && p.HourStart >= from && p.HourStart < to)
            .OrderBy(p => p.HourStart)
            .Select(p => new { p.HourStart, p.ExpectedEnergyWh, p.ModelVersion, p.GeneratedAt })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return ProductionPrediction.None;
        }

        // The period can span several model versions; the response names the newest one.
        var version = rows.MaxBy(r => r.GeneratedAt)!.ModelVersion;
        return new ProductionPrediction(version, rows.Select(r => new PredictedHour(r.HourStart, r.ExpectedEnergyWh)).ToList());
    }
}
