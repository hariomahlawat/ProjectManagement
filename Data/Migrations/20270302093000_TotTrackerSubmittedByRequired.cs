using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class TotTrackerSubmittedByRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF to_regclass('"ProjectTotRequests"') IS NOT NULL THEN
                            UPDATE "ProjectTotRequests"
                            SET "SubmittedByUserId" = ''
                            WHERE "SubmittedByUserId" IS NULL;

                            ALTER TABLE "ProjectTotRequests"
                            ALTER COLUMN "SubmittedByUserId"
                            SET NOT NULL;
                        END IF;
                    END$$;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF OBJECT_ID(N'[dbo].[ProjectTotRequests]', N'U') IS NOT NULL
                    BEGIN
                        UPDATE [ProjectTotRequests]
                        SET [SubmittedByUserId] = ''
                        WHERE [SubmittedByUserId] IS NULL;

                        ALTER TABLE [ProjectTotRequests]
                        ALTER COLUMN [SubmittedByUserId] nvarchar(450) NOT NULL;
                    END
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    "UPDATE \"ProjectTotRequests\" SET \"SubmittedByUserId\" = '' WHERE \"SubmittedByUserId\" IS NULL;");

                migrationBuilder.AlterColumn<string>(
                    name: "SubmittedByUserId",
                    table: "ProjectTotRequests",
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(450)",
                    oldMaxLength: 450,
                    oldNullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF to_regclass('"ProjectTotRequests"') IS NOT NULL THEN
                            ALTER TABLE "ProjectTotRequests"
                            ALTER COLUMN "SubmittedByUserId"
                            DROP NOT NULL;
                        END IF;
                    END$$;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF OBJECT_ID(N'[dbo].[ProjectTotRequests]', N'U') IS NOT NULL
                    BEGIN
                        ALTER TABLE [ProjectTotRequests]
                        ALTER COLUMN [SubmittedByUserId] nvarchar(450) NULL;
                    END
                    """);
            }
            else
            {
                migrationBuilder.AlterColumn<string>(
                    name: "SubmittedByUserId",
                    table: "ProjectTotRequests",
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "character varying(450)",
                    oldMaxLength: 450);
            }
        }
    }
}
