using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "commissioned_by",
                table: "installations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "commissioned_on",
                table: "installations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commissioning_note",
                table: "installations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handover_accepted_by",
                table: "installations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_revision_report_id",
                table: "installations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_revision_result",
                table: "installations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_revision_valid_until",
                table: "installations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "monitoring_enabled",
                table: "installations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "monitoring_enabled_at",
                table: "installations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "installation_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_installation_checklist_items_installation_installation_id",
                        column: x => x.installation_id,
                        principalTable: "installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installation_document_downloads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    downloaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    downloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_document_downloads", x => x.id);
                    table.ForeignKey(
                        name: "fk_installation_document_downloads_installations_installation_",
                        column: x => x.installation_id,
                        principalTable: "installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installation_revision_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspected_on = table.Column<DateOnly>(type: "date", nullable: false),
                    inspector_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    inspector_certificate_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    findings = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_revision_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_installation_revision_reports_installations_installation_id",
                        column: x => x.installation_id,
                        principalTable: "installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_installation_checklist_items_installation_id_step_code",
                table: "installation_checklist_items",
                columns: new[] { "installation_id", "step_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_installation_document_downloads_installation_id_downloaded_",
                table: "installation_document_downloads",
                columns: new[] { "installation_id", "downloaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_installation_revision_reports_installation_id_recorded_at",
                table: "installation_revision_reports",
                columns: new[] { "installation_id", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_checklist_items");

            migrationBuilder.DropTable(
                name: "installation_document_downloads");

            migrationBuilder.DropTable(
                name: "installation_revision_reports");

            migrationBuilder.DropColumn(
                name: "commissioned_by",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "commissioned_on",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "commissioning_note",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "handover_accepted_by",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "last_revision_report_id",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "last_revision_result",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "last_revision_valid_until",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "monitoring_enabled",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "monitoring_enabled_at",
                table: "installations");
        }
    }
}
