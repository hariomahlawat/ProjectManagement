using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201200000_AdminPhaseB32CalendarAndIngestion")]
public partial class AdminPhaseB32CalendarAndIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "Holidays",
            type: "bytea",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Holidays"
            SET "RowVersion" = decode(md5(random()::text || clock_timestamp()::text || "Id"::text), 'hex')
            WHERE "RowVersion" IS NULL;
            """);

        migrationBuilder.AlterColumn<byte[]>(
            name: "RowVersion",
            table: "Holidays",
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
            table: "Holidays");
    }
}
