using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261115103000_AddStageActualsUpdatedAction")]
    public partial class AddStageActualsUpdatedAction : Migration
    {
        // SECTION: Apply stage change log action update
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string allowedActions = "('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill','ActualsUpdated')";

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql($@"
ALTER TABLE \"StageChangeLogs\" DROP CONSTRAINT IF EXISTS \"CK_StageChangeLogs_Action\";
ALTER TABLE \"StageChangeLogs\" ADD CONSTRAINT \"CK_StageChangeLogs_Action\" CHECK (\"Action\" IN {allowedActions});");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql($@"
ALTER TABLE [StageChangeLogs] DROP CONSTRAINT IF EXISTS CK_StageChangeLogs_Action;
ALTER TABLE [StageChangeLogs] WITH CHECK ADD CONSTRAINT CK_StageChangeLogs_Action CHECK ([Action] IN {allowedActions});");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SECTION: SQLite skip - altering check constraints is unsupported
            }
            else
            {
                migrationBuilder.Sql($@"
ALTER TABLE \"StageChangeLogs\" DROP CONSTRAINT IF EXISTS \"CK_StageChangeLogs_Action\";
ALTER TABLE \"StageChangeLogs\" ADD CONSTRAINT \"CK_StageChangeLogs_Action\" CHECK (\"Action\" IN {allowedActions});");
            }
        }

        // SECTION: Revert stage change log action update
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            const string allowedActions = "('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill')";

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql($@"
ALTER TABLE \"StageChangeLogs\" DROP CONSTRAINT IF EXISTS \"CK_StageChangeLogs_Action\";
ALTER TABLE \"StageChangeLogs\" ADD CONSTRAINT \"CK_StageChangeLogs_Action\" CHECK (\"Action\" IN {allowedActions});");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql($@"
ALTER TABLE [StageChangeLogs] DROP CONSTRAINT IF EXISTS CK_StageChangeLogs_Action;
ALTER TABLE [StageChangeLogs] WITH CHECK ADD CONSTRAINT CK_StageChangeLogs_Action CHECK ([Action] IN {allowedActions});");
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SECTION: SQLite skip - altering check constraints is unsupported
            }
            else
            {
                migrationBuilder.Sql($@"
ALTER TABLE \"StageChangeLogs\" DROP CONSTRAINT IF EXISTS \"CK_StageChangeLogs_Action\";
ALTER TABLE \"StageChangeLogs\" ADD CONSTRAINT \"CK_StageChangeLogs_Action\" CHECK (\"Action\" IN {allowedActions});");
            }
        }
    }
}
