using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using SolarOps.Api.Features.MlIntegration;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Production;

/// <summary>Production overviews of an installation (#28): aggregated production, comparison with the prediction, CSV export.</summary>
public sealed class ProductionModule : IModule
{
    private const string PeriodDescription =
        "from/to: ISO 8601 with Z or an offset (encode + as %2B), or a date meaning local midnight in timeZone; the period is [from, to). " +
        "granularity: fiveMinutes, hour (default), day, month. timeZone: IANA id for day and month buckets, default Europe/Bratislava. " +
        "A reading belongs to the bucket of its window start (timestamp - intervalSec); energy comes from the totalWh counters, " +
        "peak power is the highest sum of the inverters' AC power. Only buckets with data are returned; an empty period gives an empty list. " +
        "A customer sees only its own installations (another one is 404).";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProductionService, ProductionService>();
        services.AddScoped<IProductionPredictionSource, StoredProductionPredictionSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/production/summary", GetSummary)
            .WithTags("Production")
            .WithName("GetProductionSummary")
            .WithSummary("Production of all visible installations for a period")
            .WithDescription(PeriodDescription + " Limits: fiveMinutes 31 days, hour 366 days, day and month 3660 days. " +
                "Power and energy are summed over the inverters of all installations the caller may see (staff: all, customer: own); " +
                "peakPowerKw is the highest summed power of a window. installationCount counts the visible installations, " +
                "also those without data. No installation or no data gives empty points, totalEnergyKwh 0 and peakPowerKw null.")
            .WithValidation<ProductionQuery>()
            .RequireAuthorization(AuthorizationPolicies.ProductionRead);

        var group = endpoints.MapGroup("/api/installations/{installationId:guid}/production").WithTags("Production");

        group.MapGet("/", Get)
            .WithName("GetInstallationProduction")
            .WithSummary("Production of an installation for a period")
            .WithDescription(PeriodDescription + " Limits: fiveMinutes 31 days, hour 366 days, day and month 3660 days.")
            .WithValidation<ProductionQuery>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.ProductionRead);

        group.MapGet("/comparison", Compare)
            .WithName("CompareInstallationProduction")
            .WithSummary("Actual production compared with the prediction")
            .WithDescription(PeriodDescription + " Granularity hour, day or month (predictions are hourly). " +
                "Pairs of actual and predicted energy per bucket, with predictions stored by the analysis of the installation (#376). " +
                "Without a stored prediction predictedEnergyKwh, deviationKwh and ratio are null and predictionAvailable is false.")
            .WithValidation<ProductionComparisonQuery>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.ProductionRead);

        group.MapGet("/export", Export)
            .WithName("ExportInstallationProduction")
            .WithSummary("Export production as CSV")
            .WithDescription(PeriodDescription + " text/csv (UTF-8) with a header row, streamed. separator: semicolon (default, decimal comma " +
                "for Slovak Excel), comma or tab (decimal point). Limits: fiveMinutes 366 days, other granularities 3660 days.")
            .WithValidation<ProductionExportQuery>()
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.ProductionRead);
    }

    internal static async Task<Ok<ProductionSummaryResponse>> GetSummary(
        [AsParameters] ProductionQuery query, IProductionService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetSummaryAsync(query.ToPeriod(), ct));

    internal static async Task<Ok<ProductionResponse>> Get(
        Guid installationId, [AsParameters] ProductionQuery query, IProductionService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(installationId, query.ToPeriod(), ct));

    internal static async Task<Ok<ProductionComparisonResponse>> Compare(
        Guid installationId, [AsParameters] ProductionComparisonQuery query, IProductionService service, CancellationToken ct) =>
        TypedResults.Ok(await service.CompareAsync(installationId, query.ToPeriod(), ct));

    internal static async Task<PushStreamHttpResult> Export(
        Guid installationId, [AsParameters] ProductionExportQuery query, IProductionService service, CancellationToken ct)
    {
        var period = query.ToPeriod();
        var separator = query.ToSeparator();

        // Access is checked before the first byte is written, so 404 is still a ProblemDetails response.
        var rows = await service.OpenExportAsync(installationId, period, ct);
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"production-{installationId:N}-{period.From:yyyyMMdd'T'HHmm'Z'}-{period.To:yyyyMMdd'T'HHmm'Z'}.csv");

        return TypedResults.Stream(
            stream => ProductionCsv.WriteAsync(stream, rows, separator, period.TimeZone, ct),
            ProductionCsv.ContentType,
            fileName);
    }
}
