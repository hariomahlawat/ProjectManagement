using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261205090000_AddProjectProliferationProfileFields")]
public partial class AddProjectProliferationProfileFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<bool>(
            name: "AvailableForProliferation",
            table: "ProjectTechStatuses",
            type: "boolean",
            nullable: true,
            oldClrType: typeof(bool),
            oldType: "boolean");

        migrationBuilder.AddColumn<string>(
            name: "ProliferationRemarks",
            table: "ProjectTechStatuses",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "ProjectTechStatuses"
            SET "AvailableForProliferation" = FALSE
            WHERE "AvailableForProliferation" IS NULL;
            """);

        migrationBuilder.AlterColumn<bool>(
            name: "AvailableForProliferation",
            table: "ProjectTechStatuses",
            type: "boolean",
            nullable: false,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldNullable: true);

        migrationBuilder.DropColumn(
            name: "ProliferationRemarks",
            table: "ProjectTechStatuses");
    }
}
