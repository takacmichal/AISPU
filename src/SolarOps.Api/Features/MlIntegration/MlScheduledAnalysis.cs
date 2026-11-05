using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>Outcome of one scheduled run over the monitored installations.</summary>
public sealed record MlScheduledAnalysisResult(int Installations, int Succeeded, int Skipped, int Failed);

/// <summary>Analyses every monitored installation (#33), so alarms and tickets appear without anybody asking for them.</summary>
public interface IMlScheduledAnalysis
{
    Task<MlScheduledAnalysisResult> RunAsync(CancellationToken ct);
}

/// <summary>
/// One scope per installation, so a failure leaves no half-tracked state for the next one. An installation that cannot be
/// analysed yet is skipped. When the service is unavailable the run stops: the others would only wait for the same timeout.
/// </summary>
internal sealed class MlScheduledAnalysis(IServiceScopeFactory scopeFactory, ILogger<MlScheduledAnalysis> logger) : IMlScheduledAnalysis
{
    public async Task<MlScheduledAnalysisResult> RunAsync(CancellationToken ct)
    {
        List<Guid> installations;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            installations = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Installation>()
                .AsNoTracking()
                .Where(i => i.MonitoringEnabled)
                .Select(i => i.Id)
                .ToListAsync(ct);
        }

        var succeeded = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var (installationId, index) in installations.Select((id, index) => (id, index)))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMlAnalysisService>().RunAsync(installationId, ct);
                succeeded++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (DomainException ex)
            {
                skipped++;
                logger.LogDebug("Skipping the analysis of installation {InstallationId}: {Reason}", installationId, ex.Message);
            }
            catch (MlServiceException ex) when (ex.IsUnavailable)
            {
                failed += installations.Count - index;
                logger.LogError(ex, "The ML service is unavailable; the analysis of {Remaining} installation(s) waits for the next run", installations.Count - index);
                break;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Scheduled analysis of installation {InstallationId} failed", installationId);
            }
        }

        return new MlScheduledAnalysisResult(installations.Count, succeeded, skipped, failed);
    }
}

/// <summary>Runs <see cref="IMlScheduledAnalysis"/> every <see cref="MlOptions.AnalysisIntervalMinutes"/> minutes.</summary>
internal sealed class MlAnalysisBackgroundService(
    IMlScheduledAnalysis analysis,
    IOptions<MlOptions> options,
    ILogger<MlAnalysisBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.AnalysisEnabled || !settings.IsConfigured)
        {
            logger.LogInformation("Scheduled ML analysis is off (Ml:AnalysisEnabled or Ml:BaseUrl).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, settings.AnalysisIntervalMinutes)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var result = await analysis.RunAsync(stoppingToken);
                    logger.LogInformation(
                        "Scheduled ML analysis finished: {Succeeded}/{Installations} installations, {Skipped} skipped, {Failed} failed",
                        result.Succeeded,
                        result.Installations,
                        result.Skipped,
                        result.Failed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Scheduled ML analysis run failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
