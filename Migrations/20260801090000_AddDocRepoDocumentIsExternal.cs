using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddDocRepoDocumentIsExternal : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExternal",
                table: "Documents",
                type: "boolean",      // you're on Postgres, so this is fine
                nullable: false,
                defaultValue: false);

            // mark existing docs that already have links as external
            migrationBuilder.Sql(@"
                UPDATE ""Documents"" d
                SET ""IsExternal"" = TRUE
                WHERE EXISTS (
                    SELECT 1
                    FROM ""DocRepoExternalLinks"" l
                    WHERE l.""DocumentId"" = d.""Id""
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExternal",
                table: "Documents");
        }
    }
}
