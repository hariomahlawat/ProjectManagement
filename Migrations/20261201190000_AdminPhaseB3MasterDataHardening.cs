using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201190000_AdminPhaseB3MasterDataHardening")]
public partial class AdminPhaseB3MasterDataHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddRowVersion(migrationBuilder, "ProjectCategories");
        AddRowVersion(migrationBuilder, "TechnicalCategories");
        AddRowVersion(migrationBuilder, "ProjectTypes");
        AddRowVersion(migrationBuilder, "SponsoringUnits");
        AddRowVersion(migrationBuilder, "LineDirectorates");

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT COALESCE("ParentId", -1), lower(btrim("Name"))
                    FROM "ProjectCategories"
                    GROUP BY COALESCE("ParentId", -1), lower(btrim("Name"))
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot apply master-data hardening: duplicate project-category names exist under the same parent (case-insensitive).';
                END IF;

                IF EXISTS (
                    SELECT COALESCE("ParentId", -1), lower(btrim("Name"))
                    FROM "TechnicalCategories"
                    GROUP BY COALESCE("ParentId", -1), lower(btrim("Name"))
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot apply master-data hardening: duplicate technical-category names exist under the same parent (case-insensitive).';
                END IF;

                IF EXISTS (SELECT lower(btrim("Name")) FROM "ProjectTypes" GROUP BY lower(btrim("Name")) HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Cannot apply master-data hardening: duplicate project-type names exist (case-insensitive).';
                END IF;

                IF EXISTS (SELECT lower(btrim("Name")) FROM "SponsoringUnits" GROUP BY lower(btrim("Name")) HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Cannot apply master-data hardening: duplicate sponsoring-unit names exist (case-insensitive).';
                END IF;

                IF EXISTS (SELECT lower(btrim("Name")) FROM "LineDirectorates" GROUP BY lower(btrim("Name")) HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Cannot apply master-data hardening: duplicate line-directorate names exist (case-insensitive).';
                END IF;

                IF EXISTS (SELECT lower(btrim("Name")) FROM "ActivityTypes" GROUP BY lower(btrim("Name")) HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Cannot apply master-data hardening: duplicate activity-type names exist (case-insensitive).';
                END IF;
            END $$;
            """);

        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX "UX_ProjectCategories_Parent_NormalizedName"
                ON "ProjectCategories" (COALESCE("ParentId", -1), lower(btrim("Name")));

            CREATE UNIQUE INDEX "UX_TechnicalCategories_Parent_NormalizedName"
                ON "TechnicalCategories" (COALESCE("ParentId", -1), lower(btrim("Name")));

            CREATE UNIQUE INDEX "UX_ProjectTypes_NormalizedName"
                ON "ProjectTypes" (lower(btrim("Name")));

            CREATE UNIQUE INDEX "UX_SponsoringUnits_NormalizedName"
                ON "SponsoringUnits" (lower(btrim("Name")));

            CREATE UNIQUE INDEX "UX_LineDirectorates_NormalizedName"
                ON "LineDirectorates" (lower(btrim("Name")));

            CREATE UNIQUE INDEX "UX_ActivityTypes_NormalizedName"
                ON "ActivityTypes" (lower(btrim("Name")));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_ProjectCategories_Parent_NormalizedName\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_TechnicalCategories_Parent_NormalizedName\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_ProjectTypes_NormalizedName\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_SponsoringUnits_NormalizedName\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_LineDirectorates_NormalizedName\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_ActivityTypes_NormalizedName\";");

        migrationBuilder.DropColumn(name: "RowVersion", table: "ProjectCategories");
        migrationBuilder.DropColumn(name: "RowVersion", table: "TechnicalCategories");
        migrationBuilder.DropColumn(name: "RowVersion", table: "ProjectTypes");
        migrationBuilder.DropColumn(name: "RowVersion", table: "SponsoringUnits");
        migrationBuilder.DropColumn(name: "RowVersion", table: "LineDirectorates");
    }

    private static void AddRowVersion(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: table,
            type: "bytea",
            nullable: true);

        migrationBuilder.Sql($"UPDATE \"{table}\" SET \"RowVersion\" = decode(md5(random()::text || clock_timestamp()::text || \"Id\"::text), 'hex') WHERE \"RowVersion\" IS NULL;");

        migrationBuilder.AlterColumn<byte[]>(
            name: "RowVersion",
            table: table,
            type: "bytea",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "bytea",
            oldNullable: true);
    }
}
