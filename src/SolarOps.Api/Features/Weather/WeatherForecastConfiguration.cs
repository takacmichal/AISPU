using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SolarOps.Api.Features.Weather;

internal sealed class WeatherForecastConfiguration : IEntityTypeConfiguration<WeatherForecast>
{
    public void Configure(EntityTypeBuilder<WeatherForecast> builder)
    {
        builder.ToTable("weather_forecasts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.InstallationId).HasColumnName("installation_id");
        builder.Property(x => x.ForecastTime).HasColumnName("forecast_time").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ShortwaveRadiationWm2).HasColumnName("shortwave_radiation_wm2");
        builder.Property(x => x.DirectNormalIrradianceWm2).HasColumnName("direct_normal_irradiance_wm2");
        builder.Property(x => x.DiffuseRadiationWm2).HasColumnName("diffuse_radiation_wm2");
        builder.Property(x => x.CloudCoverPercent).HasColumnName("cloud_cover_percent");
        builder.Property(x => x.TemperatureC).HasColumnName("temperature_c");
        builder.Property(x => x.WindSpeedMs).HasColumnName("wind_speed_ms");
        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");
        builder.Property(x => x.FetchedAt).HasColumnName("fetched_at").HasColumnType("timestamp with time zone");

        // A forecast is read per installation and ordered by hour; the unique index also guards the replace.
        builder.HasIndex(x => new { x.InstallationId, x.ForecastTime }).IsUnique();
        builder.HasIndex(x => x.ForecastTime);
    }
}
