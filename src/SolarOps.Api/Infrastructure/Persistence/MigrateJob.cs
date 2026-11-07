using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Jobs;

namespace SolarOps.Api.Infrastructure.Persistence;

/// <summary>
/// Applies the committed EF Core migrations (ADR #277). In Azure the CD pipeline starts this job before a new API
/// revision; the API itself migrates only in Development.
/// </summary>
internal sealed class MigrateJob(AppDbContext db) : IJob
{
    public string Name => "migrate";

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        return true;
    }
}
