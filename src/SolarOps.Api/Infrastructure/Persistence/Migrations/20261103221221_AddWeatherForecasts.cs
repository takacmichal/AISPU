using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeatherForecasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weather_forecasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forecast_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    shortwave_radiation_wm2 = table.Column<double>(type: "double precision", nullable: true),
                    direct_normal_irradiance_wm2 = table.Column<double>(type: "double precision", nullable: true),
                    diffuse_radiation_wm2 = table.Column<double>(type: "double precision", nullable: true),
                    cloud_cover_percent = table.Column<double>(type: "double precision", nullable: true),
                    temperature_c = table.Column<double>(type: "double precision", nullable: true),
                    wind_speed_ms = table.Column<double>(type: "double precision", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weather_forecasts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_weather_forecasts_forecast_time",
                table: "weather_forecasts",
                column: "forecast_time");

            migrationBuilder.CreateIndex(
                name: "ix_weather_forecasts_installation_id_forecast_time",
                table: "weather_forecasts",
                columns: new[] { "installation_id", "forecast_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weather_forecasts");
        }
    }
}
