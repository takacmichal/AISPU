namespace SolarOps.Api.Infrastructure.Jobs;

/// <summary>Registers and runs <see cref="IJob"/>s; the exit code tells Container Apps whether the execution succeeded.</summary>
public static class JobRunner
{
    /// <summary>Configuration key with the job name, set by the command line argument <c>--job &lt;name&gt;</c>.</summary>
    public const string ConfigurationKey = "Job";

    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int UnknownJob = 2;

    public static IServiceCollection AddJob<TJob>(this IServiceCollection services)
        where TJob : class, IJob =>
        services.AddScoped<IJob, TJob>();

    /// <summary>Runs the job called <paramref name="name"/> (case-insensitive) in its own scope and returns the exit code.</summary>
    public static async Task<int> RunJobAsync(this IServiceProvider services, string name, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(JobRunner).FullName!);
        var jobs = scope.ServiceProvider.GetServices<IJob>().ToList();

        var job = jobs.SingleOrDefault(j => string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            logger.LogError("Unknown job {Job}. Known jobs: {Jobs}", name, string.Join(", ", jobs.Select(j => j.Name).Order()));
            return UnknownJob;
        }

        logger.LogInformation("Job {Job} started", job.Name);
        try
        {
            if (await job.RunAsync(cancellationToken))
            {
                logger.LogInformation("Job {Job} succeeded", job.Name);
                return Succeeded;
            }

            logger.LogError("Job {Job} failed", job.Name);
            return Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Job {Job} failed", job.Name);
            return Failed;
        }
    }
}
