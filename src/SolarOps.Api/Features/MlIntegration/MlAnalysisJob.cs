using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Jobs;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>
/// One run of the scheduled analysis for a cron Container Apps Job (ADR #277). Ignores <see cref="MlOptions.AnalysisEnabled"/>,
/// which only turns off the in-process schedule of the API.
/// </summary>
internal sealed class MlAnalysisJob(IMlScheduledAnalysis analysis, IOptions<MlOptions> options, ILogger<MlAnalysisJob> logger) : IJob
{
    public string Name => "ml-analysis";

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogError("Ml:BaseUrl is not set, the ML analysis cannot run");
            return false;
        }

        var result = await analysis.RunAsync(cancellationToken);
        logger.LogInformation(
            "ML analysis finished: {Succeeded}/{Installations} installations, {Skipped} skipped, {Failed} failed",
            result.Succeeded,
            result.Installations,
            result.Skipped,
            result.Failed);
        return result.Failed == 0;
    }
}
