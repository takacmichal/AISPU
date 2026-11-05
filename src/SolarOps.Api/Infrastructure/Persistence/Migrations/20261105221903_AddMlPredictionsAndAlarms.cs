using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMlPredictionsAndAlarms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "production_predictions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_power_w = table.Column<double>(type: "double precision", nullable: false),
                    expected_energy_wh = table.Column<double>(type: "double precision", nullable: false),
                    ghi_wm2 = table.Column<double>(type: "double precision", nullable: true),
                    cloud_cover_pct = table.Column<double>(type: "double precision", nullable: true),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_predictions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "underproduction_alarms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_underproduction_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ongoing = table.Column<bool>(type: "boolean", nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ticket_priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    median_ratio = table.Column<double>(type: "double precision", nullable: false),
                    min_ratio = table.Column<double>(type: "double precision", nullable: false),
                    underproducing_hours = table.Column<int>(type: "integer", nullable: false),
                    valid_hours = table.Column<int>(type: "integer", nullable: false),
                    duration_hours = table.Column<double>(type: "double precision", nullable: false),
                    expected_kwh = table.Column<double>(type: "double precision", nullable: false),
                    actual_kwh = table.Column<double>(type: "double precision", nullable: false),
                    estimated_lost_kwh = table.Column<double>(type: "double precision", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_underproduction_alarms", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_production_predictions_installation_id_hour_start",
                table: "production_predictions",
                columns: new[] { "installation_id", "hour_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_underproduction_alarms_installation_id_start",
                table: "underproduction_alarms",
                columns: new[] { "installation_id", "start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_predictions");

            migrationBuilder.DropTable(
                name: "underproduction_alarms");
        }
    }
}
