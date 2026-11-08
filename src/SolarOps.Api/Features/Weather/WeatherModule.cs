using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Jobs;
using SolarOps.Api.Infrastructure.Modules;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Weather;

/// <summary>
/// Weather forecast collection (#104): a daily background job stores a 48-hour hourly forecast for every
/// installation, using the Open-Meteo Forecast API chosen in ADR-180.
/// </summary>
public sealed class WeatherModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WeatherOptions>()
            .Bind(configuration.GetSection(WeatherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IWeatherForecastClient, OpenMeteoWeatherForecastClient>()
            .ConfigureHttpClient((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<WeatherOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            });

        services.AddHttpClient<IWeatherArchiveClient, OpenMeteoWeatherArchiveClient>()
            .ConfigureHttpClient((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<WeatherOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            });

        services.AddScoped<IWeatherForecastRefreshService, WeatherForecastRefreshService>();
        services.AddScoped<IWeatherBackfillService, WeatherBackfillService>();
        services.AddHostedService<WeatherForecastBackgroundService>();
        services.AddJob<WeatherRefreshJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/weather/backfill", BackfillAsync)
            .WithTags("Weather")
            .WithName("BackfillWeather")
            .WithSummary("Store past hourly weather from the archive")
            .WithDescription("Fetches the Open-Meteo archive (ERA5) for the days from..to (UTC, inclusive, at most 31 days, to before today) "
                + "for every installation with coordinates and stores the hours like the forecast. Idempotent: rerunning replaces the same hours. "
                + "Synchronous; returns the totals and one item per installation. Stores no predictions and creates no alarms or tickets.")
            .WithValidation<HistoricalPeriod>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    internal static async Task<Ok<WeatherBackfillResult>> BackfillAsync(
        HistoricalPeriod period, IWeatherBackfillService service, CancellationToken ct) =>
        TypedResults.Ok(await service.BackfillAsync(period, ct));
}
