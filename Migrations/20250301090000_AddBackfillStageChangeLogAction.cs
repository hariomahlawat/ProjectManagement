using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250301090000_AddBackfillStageChangeLogAction")]
    public partial class AddBackfillStageChangeLogAction : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StageChangeLogs_Action",
                table: "StageChangeLogs");

            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "[Action] IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill')");
            }
            else if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "\"Action\" IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill')");
            }
            else
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "Action IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill')");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StageChangeLogs_Action",
                table: "StageChangeLogs");

            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "[Action] IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill')");
            }
            else if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "\"Action\" IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill')");
            }
            else
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "Action IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill')");
            }
        }
    }
}
