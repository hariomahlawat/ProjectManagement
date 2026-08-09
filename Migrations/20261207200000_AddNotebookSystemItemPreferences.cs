using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207200000_AddNotebookSystemItemPreferences")]
public sealed class AddNotebookSystemItemPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NotebookSystemItemPreferences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                SystemItemKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ShowInHome = table.Column<bool>(type: "boolean", nullable: false),
                IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                HomePosition = table.Column<int>(type: "integer", nullable: false),
                ColorKey = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotebookSystemItemPreferences", x => x.Id);
                table.CheckConstraint("CK_NotebookSystemItemPreferences_HomePosition", "\"HomePosition\" >= 0");
                table.ForeignKey(
                    name: "FK_NotebookSystemItemPreferences_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "NotebookSystemItemTags",
            columns: table => new
            {
                PreferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                NotebookTagId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotebookSystemItemTags", x => new { x.PreferenceId, x.NotebookTagId });
                table.ForeignKey(
                    name: "FK_NotebookSystemItemTags_NotebookSystemItemPreferences_PreferenceId",
                    column: x => x.PreferenceId,
                    principalTable: "NotebookSystemItemPreferences",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_NotebookSystemItemTags_NotebookTags_NotebookTagId",
                    column: x => x.NotebookTagId,
                    principalTable: "NotebookTags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NotebookSystemItemPreferences_UserId_SystemItemKey",
            table: "NotebookSystemItemPreferences",
            columns: new[] { "UserId", "SystemItemKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_NotebookSystemItemTags_NotebookTagId",
            table: "NotebookSystemItemTags",
            column: "NotebookTagId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NotebookSystemItemTags");
        migrationBuilder.DropTable(name: "NotebookSystemItemPreferences");
    }
}
