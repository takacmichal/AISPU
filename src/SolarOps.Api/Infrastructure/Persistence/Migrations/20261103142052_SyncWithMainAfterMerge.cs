using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncWithMainAfterMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_telemetry_readings_installation_id_ts",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "ac_current_a",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "installation_id",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "interval_seconds",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "telemetry_readings");

            migrationBuilder.RenameColumn(
                name: "power_factor",
                table: "telemetry_readings",
                newName: "voltage_v");

            migrationBuilder.RenameColumn(
                name: "error_code",
                table: "telemetry_readings",
                newName: "interval_sec");

            migrationBuilder.RenameColumn(
                name: "ac_voltage_v",
                table: "telemetry_readings",
                newName: "current_a");

            migrationBuilder.AlterColumn<double>(
                name: "total_wh",
                table: "telemetry_readings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE telemetry_readings
                ALTER COLUMN status TYPE smallint
                USING CASE status
                    WHEN 'OFF' THEN 1
                    WHEN 'SLEEPING' THEN 2
                    WHEN 'STARTING' THEN 3
                    WHEN 'MPPT' THEN 4
                    WHEN 'THROTTLED' THEN 5
                    WHEN 'SHUTTING_DOWN' THEN 6
                    WHEN 'FAULT' THEN 7
                    WHEN 'STANDBY' THEN 8
                    ELSE 0
                END;
                """);

            migrationBuilder.AlterColumn<float>(
                name: "ac_power_w",
                table: "telemetry_readings",
                type: "real",
                nullable: false,
                defaultValue: 0f,
                oldClrType: typeof(float),
                oldType: "real",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "events",
                table: "telemetry_readings",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "received_at",
                table: "telemetry_readings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "telemetry_readings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "strings",
                table: "telemetry_readings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vendor_status_code",
                table: "telemetry_readings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "design_id",
                table: "quotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "design_version_number",
                table: "quotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "needs_recalculation",
                table: "quotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recalculation_requested_at",
                table: "quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "string_id",
                table: "installation_devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "fve_designs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                table: "fve_designs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approved_version_number",
                table: "fve_designs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rework_at",
                table: "fve_designs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rework_by",
                table: "fve_designs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rework_reason",
                table: "fve_designs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "conditions_reviewed_at",
                table: "connection_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "contract_sent_at",
                table: "connection_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "connection_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "connection_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "proposal_due_date",
                table: "connection_requests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "connection_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_api_keys_installation_installation_id",
                        column: x => x.installation_id,
                        principalTable: "installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "panel_strings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mppt_input = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panel_strings", x => x.id);
                    table.ForeignKey(
                        name: "fk_panel_strings_installations_installation_id",
                        column: x => x.installation_id,
                        principalTable: "installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_design_id",
                table: "quotes",
                column: "design_id");

            migrationBuilder.CreateIndex(
                name: "ix_installation_devices_string_id",
                table: "installation_devices",
                column: "string_id");

            migrationBuilder.CreateIndex(
                name: "ux_device_api_keys_active_installation",
                table: "device_api_keys",
                column: "installation_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_panel_strings_installation_id_number",
                table: "panel_strings",
                columns: new[] { "installation_id", "number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_installation_devices_panel_string_string_id",
                table: "installation_devices",
                column: "string_id",
                principalTable: "panel_strings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_installation_devices_panel_string_string_id",
                table: "installation_devices");

            migrationBuilder.DropTable(
                name: "device_api_keys");

            migrationBuilder.DropTable(
                name: "panel_strings");

            migrationBuilder.DropIndex(
                name: "ix_quotes_design_id",
                table: "quotes");

            migrationBuilder.DropIndex(
                name: "ix_installation_devices_string_id",
                table: "installation_devices");

            migrationBuilder.DropColumn(
                name: "events",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "source",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "strings",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "vendor_status_code",
                table: "telemetry_readings");

            migrationBuilder.DropColumn(
                name: "design_id",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "design_version_number",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "needs_recalculation",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "recalculation_requested_at",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "string_id",
                table: "installation_devices");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "fve_designs");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "fve_designs");

            migrationBuilder.DropColumn(
                name: "approved_version_number",
                table: "fve_designs");

            migrationBuilder.DropColumn(
                name: "rework_at",
                table: "fve_designs");

            migrationBuilder.DropColumn(
                name: "rework_by",
                table: "fve_designs");

            migrationBuilder.DropColumn(
                name: "rework_reason",
                table: "fve_designs");

            migrationBuilder.DropColumn(
                name: "conditions_reviewed_at",
                table: "connection_requests");

            migrationBuilder.DropColumn(
                name: "contract_sent_at",
                table: "connection_requests");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "connection_requests");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "connection_requests");

            migrationBuilder.DropColumn(
                name: "proposal_due_date",
                table: "connection_requests");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "connection_requests");

            migrationBuilder.RenameColumn(
                name: "voltage_v",
                table: "telemetry_readings",
                newName: "power_factor");

            migrationBuilder.RenameColumn(
                name: "interval_sec",
                table: "telemetry_readings",
                newName: "error_code");

            migrationBuilder.RenameColumn(
                name: "current_a",
                table: "telemetry_readings",
                newName: "ac_voltage_v");

            migrationBuilder.AlterColumn<double>(
                name: "total_wh",
                table: "telemetry_readings",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "telemetry_readings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<float>(
                name: "ac_power_w",
                table: "telemetry_readings",
                type: "real",
                nullable: true,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.AddColumn<float>(
                name: "ac_current_a",
                table: "telemetry_readings",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installation_id",
                table: "telemetry_readings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<short>(
                name: "interval_seconds",
                table: "telemetry_readings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)300);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "telemetry_readings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_readings_installation_id_ts",
                table: "telemetry_readings",
                columns: new[] { "installation_id", "ts" })
                .Annotation("Npgsql:IndexInclude", new[] { "ac_power_w" });
        }
    }
}
