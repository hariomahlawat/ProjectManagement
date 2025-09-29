using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class FixStageDatesAndCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "ActualStart",
                table: "ProjectStages",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: false);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "CompletedOn",
                table: "ProjectStages",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: false);

            migrationBuilder.AlterColumn<bool>(
                name: "RequiresBackfill",
                table: "ProjectStages",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: false);

            migrationBuilder.Sql(@"
        UPDATE ""ProjectStages""
        SET ""RequiresBackfill"" = TRUE
        WHERE ""Status"" = 'Completed'
          AND (""ActualStart"" IS NULL OR ""CompletedOn"" IS NULL);
    ");

            migrationBuilder.Sql(@"
        DO $$
        DECLARE r record;
        BEGIN
          FOR r IN
            SELECT conname FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'public' AND t.relname = 'ProjectStages'
              AND conname LIKE 'CK_ProjectStages_CompletedHasDate%'
          LOOP
            EXECUTE format('ALTER TABLE ""ProjectStages"" DROP CONSTRAINT %I;', r.conname);
          END LOOP;
        END $$;
    ");

            migrationBuilder.Sql(@"
        ALTER TABLE ""ProjectStages""
        ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
        CHECK (
          ""Status"" <> 'Completed'
          OR (""CompletedOn"" IS NOT NULL AND ""ActualStart"" IS NOT NULL)
          OR ""RequiresBackfill"" IS TRUE
        );
    ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ProjectStages"" DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";");

            migrationBuilder.AlterColumn<bool>(
                name: "RequiresBackfill",
                table: "ProjectStages",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "CompletedOn",
                table: "ProjectStages",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ActualStart",
                table: "ProjectStages",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
