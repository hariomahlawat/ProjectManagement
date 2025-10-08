using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class LegacyProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ProjectTots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotStarted",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ProjectStages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "NotStarted");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCelebrationsInCalendar",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'ProjectDocumentRequests'
                          AND column_name = 'TotId'
                    ) THEN
                        CREATE INDEX IF NOT EXISTS "IX_ProjectDocumentRequests_TotId"
                            ON "ProjectDocumentRequests" ("TotId");
                    END IF;
                END
                $$;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'public'
                          AND indexname = 'IX_ProjectDocumentRequests_TotId'
                    ) THEN
                        DROP INDEX "IX_ProjectDocumentRequests_TotId";
                    END IF;
                END
                $$;
                """
            );

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ProjectTots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "NotStarted");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ProjectStages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotStarted",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCelebrationsInCalendar",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
