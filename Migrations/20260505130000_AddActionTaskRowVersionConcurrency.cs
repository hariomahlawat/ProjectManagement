using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddActionTaskRowVersionConcurrency : Migration
    {
        // SECTION: Add optimistic concurrency token to action tasks
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ActionTasks",
                type: "bytea",
                rowVersion: true,
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE ""ActionTasks""
                  SET ""RowVersion"" = decode(md5('ActionTasks.RowVersion:' || ""Id""::text || ':' || clock_timestamp()::text), 'hex')
                  WHERE ""RowVersion"" IS NULL OR length(""RowVersion"") = 0;");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "ActionTasks",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "decode(md5(random()::text || clock_timestamp()::text), 'hex')",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true,
                oldNullable: true);
        }

        // SECTION: Remove optimistic concurrency token from action tasks
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ActionTasks");
        }
    }
}
