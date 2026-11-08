namespace SolarOps.Api.Infrastructure.Jobs;

/// <summary>
/// Work run once by a Container Apps Job (ADR #277): the API image started with <c>--job &lt;name&gt;</c>
/// runs the job and exits instead of starting the web server. Modules register jobs with
/// <see cref="JobRunner.AddJob{TJob}"/>.
/// </summary>
public interface IJob
{
    /// <summary>Name on the command line, e.g. <c>ml-analysis</c>.</summary>
    string Name { get; }

    /// <summary>Runs the job once. Returns false when the run failed, so the job execution is marked as failed.</summary>
    Task<bool> RunAsync(CancellationToken cancellationToken);
}
