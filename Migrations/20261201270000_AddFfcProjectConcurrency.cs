using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201270000_AddFfcProjectConcurrency")]
public partial class AddFfcProjectConcurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "FfcProjects",
            type: "bytea",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "FfcProjects"
            SET "RowVersion" = decode(
                md5(random()::text || clock_timestamp()::text || "Id"::text),
                'hex')
            WHERE "RowVersion" IS NULL;
            """);

        migrationBuilder.AlterColumn<byte[]>(
            name: "RowVersion",
            table: "FfcProjects",
            type: "bytea",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "bytea",
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "FfcProjects");
    }
}
