using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarOps.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixInstallationChecklistItemForeignKeyName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_installation_checklist_items_installation_installation_id",
                table: "installation_checklist_items");

            migrationBuilder.AddForeignKey(
                name: "fk_installation_checklist_items_installations_installation_id",
                table: "installation_checklist_items",
                column: "installation_id",
                principalTable: "installations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_installation_checklist_items_installations_installation_id",
                table: "installation_checklist_items");

            migrationBuilder.AddForeignKey(
                name: "fk_installation_checklist_items_installation_installation_id",
                table: "installation_checklist_items",
                column: "installation_id",
                principalTable: "installations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
