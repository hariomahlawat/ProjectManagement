using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250214000000_AddAutoBackfillStageChangeLogAction")]
    public partial class AddAutoBackfillStageChangeLogAction : Migration
    {
        /// <inheritdoc />
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

        /// <inheritdoc />
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
                    sql: "[Action] IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded')");
            }
            else if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "\"Action\" IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded')");
            }
            else
            {
                migrationBuilder.AddCheckConstraint(
                    name: "CK_StageChangeLogs_Action",
                    table: "StageChangeLogs",
                    sql: "Action IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded')");
            }
        }
    }
}
