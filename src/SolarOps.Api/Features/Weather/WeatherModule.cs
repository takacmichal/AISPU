using Microsoft.Extensions.Options;
using SolarOps.Api.Infrastructure.Modules;

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

        services.AddScoped<IWeatherForecastRefreshService, WeatherForecastRefreshService>();
        services.AddHostedService<WeatherForecastBackgroundService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // No public endpoint yet; forecasts are consumed by the prediction service (follow-up stories).
    }
}
