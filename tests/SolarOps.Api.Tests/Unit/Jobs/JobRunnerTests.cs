using Microsoft.Extensions.DependencyInjection;
using SolarOps.Api.Infrastructure.Jobs;

namespace SolarOps.Api.Tests.Unit.Jobs;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task RunJobAsync_RunsTheNamedJob_IgnoringCase()
    {
        var services = Build(new FakeJob("first", () => true), new FakeJob("second", () => true));

        var exitCode = await services.RunJobAsync("SECOND");

        Assert.Equal(JobRunner.Succeeded, exitCode);
        Assert.Equal(["second"], RanJobs(services));
    }

    [Fact]
    public async Task RunJobAsync_JobReportsFailure_ReturnsFailed()
    {
        var services = Build(new FakeJob("broken", () => false));

        Assert.Equal(JobRunner.Failed, await services.RunJobAsync("broken"));
    }

    [Fact]
    public async Task RunJobAsync_JobThrows_ReturnsFailed()
    {
        var services = Build(new FakeJob("throwing", () => throw new InvalidOperationException("boom")));

        Assert.Equal(JobRunner.Failed, await services.RunJobAsync("throwing"));
    }

    [Fact]
    public async Task RunJobAsync_UnknownName_ReturnsUnknownJob_AndRunsNothing()
    {
        var services = Build(new FakeJob("known", () => true));

        Assert.Equal(JobRunner.UnknownJob, await services.RunJobAsync("missing"));
        Assert.Empty(RanJobs(services));
    }

    private static ServiceProvider Build(params FakeJob[] jobs)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton(new JobLog());
        foreach (var job in jobs)
        {
            services.AddScoped<IJob>(sp => job with { Log = sp.GetRequiredService<JobLog>() });
        }

        return services.BuildServiceProvider();
    }

    private static List<string> RanJobs(IServiceProvider services) => services.GetRequiredService<JobLog>().Ran;

    private sealed class JobLog
    {
        public List<string> Ran { get; } = [];
    }

    private sealed record FakeJob(string Name, Func<bool> Outcome) : IJob
    {
        public JobLog? Log { get; init; }

        public Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            Log?.Ran.Add(Name);
            return Task.FromResult(Outcome());
        }
    }
}
