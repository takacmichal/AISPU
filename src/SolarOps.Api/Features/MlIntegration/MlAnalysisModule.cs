using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>
/// Integration with the ML service (#376, ADR #196): a typed client, an analysis of an installation on demand and on a schedule,
/// the stored underproduction alarms and service tickets created from them (#33).
/// </summary>
public sealed class MlAnalysisModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MlOptions>()
            .Bind(configuration.GetSection(MlOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.HasValidBaseUrl(), "Ml:BaseUrl must be empty or an absolute http/https address.")
            .Validate(o => o.TicketMediumLostKwh <= o.TicketHighLostKwh, "Ml:TicketMediumLostKwh must not exceed Ml:TicketHighLostKwh.")
            .ValidateOnStart();

        services.AddHttpClient<IMlServiceClient, MlServiceClient>()
            .ConfigureHttpClient((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<MlOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            });

        services.AddScoped<IMlAnalysisService, MlAnalysisService>();
        services.AddSingleton<IMlScheduledAnalysis, MlScheduledAnalysis>();
        services.AddHostedService<MlAnalysisBackgroundService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/installations/{installationId:guid}").WithTags("ML analysis");

        group.MapPost("/analysis", RunAnalysis)
            .WithName("RunInstallationAnalysis")
            .WithSummary("Run the ML prediction and underproduction detection")
            .WithDescription("Predicts production from the stored weather forecast, then evaluates the finished hours with the detection of " +
                "ADR #194 and stores both results. Repeating the call overwrites the predictions of the same hours and updates the same alarms. " +
                "An ongoing alarm creates a service ticket with origin AnomalyDetected, or an internal comment on an open ticket of the installation. " +
                "503 when the ML service is unavailable, 502 when it returns an unusable answer; the data stored earlier stays available.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization(AuthorizationPolicies.ProductionAnalyze);

        group.MapGet("/underproduction-alarms", ListAlarms)
            .WithName("ListUnderproductionAlarms")
            .WithSummary("Underproduction alarms of an installation")
            .WithDescription("Stored results of the last analyses, newest first, at most 100; the ML service is not called. " +
                "A customer sees only its own installations (another one is 404).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.ProductionRead);

        endpoints.MapGet("/api/underproduction-alarms", ListAllAlarms)
            .WithTags("ML analysis")
            .WithName("ListAllUnderproductionAlarms")
            .WithSummary("Underproduction alarms of all installations")
            .WithDescription("Stored results of the last analyses, newest first, at most 100; light enough for the dashboard to poll every minute " +
                "(ADR #222), the ML service is not called. severity: Low, Medium, High. ongoing: true or false. " +
                "A customer gets only the alarms of its own installations.")
            .RequireAuthorization(AuthorizationPolicies.ProductionRead);
    }

    internal static async Task<Results<Ok<MlAnalysisResponse>, ProblemHttpResult>> RunAnalysis(
        Guid installationId, IMlAnalysisService service, ILoggerFactory loggers, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await service.RunAsync(installationId, ct));
        }
        catch (MlServiceException ex)
        {
            // The answer of the service stays in the log; it is not meant for API clients.
            loggers.CreateLogger<MlAnalysisModule>().LogError(ex, "Analysis of installation {InstallationId} failed", installationId);
            return ex.IsUnavailable
                ? TypedResults.Problem("The ML service is unavailable. Try again later.", statusCode: StatusCodes.Status503ServiceUnavailable)
                : TypedResults.Problem("The ML service returned an unusable answer.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static async Task<Ok<UnderproductionAlarmListResponse>> ListAllAlarms(
        UnderproductionSeverity? severity, bool? ongoing, IMlAnalysisService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAlarmsAsync(severity, ongoing, ct));

    internal static async Task<Ok<UnderproductionAlarmListResponse>> ListAlarms(
        Guid installationId, IMlAnalysisService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAlarmsAsync(installationId, ct));
}
