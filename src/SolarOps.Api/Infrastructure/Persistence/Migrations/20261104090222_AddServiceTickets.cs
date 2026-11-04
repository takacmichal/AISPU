using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_ticket_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_ticket_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_ticket_attachments_service_ticket_service_ticket_id",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_ticket_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_ticket_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_ticket_comments_service_ticket_service_ticket_id",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_ticket_state_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_ticket_state_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_ticket_state_changes_service_tickets_service_ticket",
                        column: x => x.service_ticket_id,
                        principalTable: "service_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_attachments_service_ticket_id",
                table: "service_ticket_attachments",
                column: "service_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_comments_service_ticket_id_created_at",
                table: "service_ticket_comments",
                columns: new[] { "service_ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_state_changes_service_ticket_id_changed_at",
                table: "service_ticket_state_changes",
                columns: new[] { "service_ticket_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_tickets_created_at",
                table: "service_tickets",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_service_tickets_customer_id",
                table: "service_tickets",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_tickets_installation_id",
                table: "service_tickets",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_tickets_state_priority",
                table: "service_tickets",
                columns: new[] { "state", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_ticket_attachments");

            migrationBuilder.DropTable(
                name: "service_ticket_comments");

            migrationBuilder.DropTable(
                name: "service_ticket_state_changes");

            migrationBuilder.DropTable(
                name: "service_tickets");
        }
    }
}
