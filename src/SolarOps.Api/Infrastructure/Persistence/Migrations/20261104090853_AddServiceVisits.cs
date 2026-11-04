using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_ticket_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_ticket_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_ticket_assignments_service_ticket_service_ticket_id",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_ticket_confirmations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_ticket_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_ticket_confirmations_service_tickets_service_ticket",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    planned_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planned_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: false),
                    planned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cause = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    findings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    work_done = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    departed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_visits", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_visits_service_tickets_service_ticket_id",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_visit_device_replacements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    replaced_by = table.Column<Guid>(type: "uuid", nullable: false),
                    replaced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_visit_device_replacements", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_visit_device_replacements_service_tickets_service_t",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_service_visit_device_replacements_service_visits_service_vi",
                        column: x => x.service_visit_id,
                        principalTable: "service_visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_visit_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_visit_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_visit_materials_service_tickets_service_ticket_id",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_service_visit_materials_service_visits_service_visit_id",
                        column: x => x.service_visit_id,
                        principalTable: "service_visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_visit_reschedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    previous_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_visit_reschedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_visit_reschedules_service_visits_service_visit_id",
                        column: x => x.service_visit_id,
                        principalTable: "service_visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_assignments_service_ticket_id",
                table: "service_ticket_assignments",
                column: "service_ticket_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_assignments_technician_id",
                table: "service_ticket_assignments",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_confirmations_service_ticket_id_decided_at",
                table: "service_ticket_confirmations",
                columns: new[] { "service_ticket_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_visit_device_replacements_service_ticket_id",
                table: "service_visit_device_replacements",
                column: "service_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_visit_device_replacements_service_visit_id",
                table: "service_visit_device_replacements",
                column: "service_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_visit_materials_service_ticket_id",
                table: "service_visit_materials",
                column: "service_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_visit_materials_service_visit_id",
                table: "service_visit_materials",
                column: "service_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_visit_reschedules_service_visit_id_changed_at",
                table: "service_visit_reschedules",
                columns: new[] { "service_visit_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_visits_service_ticket_id",
                table: "service_visits",
                column: "service_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_visits_technician_id_planned_start",
                table: "service_visits",
                columns: new[] { "technician_id", "planned_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_ticket_assignments");

            migrationBuilder.DropTable(
                name: "service_ticket_confirmations");

            migrationBuilder.DropTable(
                name: "service_visit_device_replacements");

            migrationBuilder.DropTable(
                name: "service_visit_materials");

            migrationBuilder.DropTable(
                name: "service_visit_reschedules");

            migrationBuilder.DropTable(
                name: "service_visits");
        }
    }
}
