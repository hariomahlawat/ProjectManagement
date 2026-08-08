using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207190000_StabilizeNotebookModule")]
public partial class StabilizeNotebookModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SECTION: Durable marker for the one-time legacy Todo import.
        migrationBuilder.CreateTable(
            name: "NotebookMigrationStates",
            columns: table => new
            {
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                MigrationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ImportedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotebookMigrationStates", x => new { x.UserId, x.MigrationKey });
                table.ForeignKey(
                    name: "FK_NotebookMigrationStates_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // SECTION: Import any remaining live legacy Todos exactly once.
        // Reminder is metadata, therefore imported Todos are ordinary Note items.
        // UUIDs are deterministic so a migration retry cannot manufacture a second record.
        migrationBuilder.Sql(
            """
            INSERT INTO "NotebookItems" (
                "Id", "OwnerId", "Title", "BodyMarkdown", "Type", "Status", "Priority",
                "ReminderAtUtc", "CompletedAtUtc", "IsPinned", "IsFavorite", "ColorKey",
                "SortOrder", "LegacyTodoItemId", "ClientRequestId", "CreatedAtUtc", "Version",
                "UpdatedAtUtc", "ArchivedAtUtc", "DeletedAtUtc")
            SELECT
                md5(t."OwnerId" || ':todo:' || t."Id"::text)::uuid,
                t."OwnerId",
                t."Title",
                NULL,
                0::smallint,
                CASE WHEN t."Status" = 1 THEN 1 ELSE 0 END::smallint,
                t."Priority"::smallint,
                t."DueAtUtc",
                t."CompletedUtc",
                t."IsPinned",
                FALSE,
                'amber',
                (((ROW_NUMBER() OVER (
                    PARTITION BY t."OwnerId", t."IsPinned"
                    ORDER BY t."OrderIndex", t."CreatedUtc", t."Id"
                )) - 1) * 1024)::integer,
                t."Id",
                NULL,
                t."CreatedUtc",
                md5(t."OwnerId" || ':todo-version:' || t."Id"::text)::uuid,
                t."UpdatedUtc",
                NULL,
                NULL
            FROM "TodoItems" t
            WHERE t."DeletedUtc" IS NULL
              AND EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."OwnerId")
              AND NOT EXISTS (
                  SELECT 1
                  FROM "NotebookItems" n
                  WHERE n."OwnerId" = t."OwnerId"
                    AND n."LegacyTodoItemId" = t."Id"
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM "NotebookItems" n
                  WHERE n."Id" = md5(t."OwnerId" || ':todo:' || t."Id"::text)::uuid
              );
            """);

        // SECTION: Preserve semantic hints from retired content types as labels.
        migrationBuilder.Sql(
            """
            INSERT INTO "NotebookTags" ("OwnerId", "Name", "NormalizedName")
            SELECT DISTINCT n."OwnerId", 'Idea', 'IDEA'
            FROM "NotebookItems" n
            WHERE n."Type" = 4
            ON CONFLICT ("OwnerId", "NormalizedName") DO NOTHING;

            INSERT INTO "NotebookItemTags" ("NotebookItemId", "NotebookTagId")
            SELECT n."Id", t."Id"
            FROM "NotebookItems" n
            JOIN "NotebookTags" t
              ON t."OwnerId" = n."OwnerId" AND t."NormalizedName" = 'IDEA'
            WHERE n."Type" = 4
            ON CONFLICT ("NotebookItemId", "NotebookTagId") DO NOTHING;

            INSERT INTO "NotebookTags" ("OwnerId", "Name", "NormalizedName")
            SELECT DISTINCT n."OwnerId", 'Draft', 'DRAFT'
            FROM "NotebookItems" n
            WHERE n."Type" = 5
            ON CONFLICT ("OwnerId", "NormalizedName") DO NOTHING;

            INSERT INTO "NotebookItemTags" ("NotebookItemId", "NotebookTagId")
            SELECT n."Id", t."Id"
            FROM "NotebookItems" n
            JOIN "NotebookTags" t
              ON t."OwnerId" = n."OwnerId" AND t."NormalizedName" = 'DRAFT'
            WHERE n."Type" = 5
            ON CONFLICT ("NotebookItemId", "NotebookTagId") DO NOTHING;
            """);

        // SECTION: Final content model is Note or Checklist. Reminder, colour and pin are metadata.
        migrationBuilder.Sql(
            """
            UPDATE "NotebookItems"
            SET "Type" = 0
            WHERE "Type" IN (1, 3, 4, 5);

            UPDATE "NotebookItems"
            SET "ColorKey" = 'white'
            WHERE "ColorKey" IS NOT NULL
              AND LOWER(BTRIM("ColorKey")) NOT IN ('white', 'blue', 'amber', 'green', 'rose', 'slate');

            UPDATE "NotebookItems"
            SET "ColorKey" = LOWER(BTRIM("ColorKey"))
            WHERE "ColorKey" IS NOT NULL;
            """);

        // SECTION: Normalise active board order while preserving the order users saw before this migration.
        migrationBuilder.Sql(
            """
            WITH ranked AS (
                SELECT
                    n."Id",
                    (((ROW_NUMBER() OVER (
                        PARTITION BY n."OwnerId", n."IsPinned"
                        ORDER BY
                            CASE WHEN n."SortOrder" = 0 THEN 2147483647 ELSE n."SortOrder" END,
                            n."UpdatedAtUtc" DESC,
                            n."Id"
                    )) - 1) * 1024)::integer AS "NewSortOrder"
                FROM "NotebookItems" n
                WHERE n."DeletedAtUtc" IS NULL
                  AND n."Status" = 0
            )
            UPDATE "NotebookItems" n
            SET "SortOrder" = ranked."NewSortOrder"
            FROM ranked
            WHERE n."Id" = ranked."Id";
            """);

        // SECTION: Existing race-created duplicates are retained as notes but only one keeps
        // the legacy identity. This avoids deleting user content before enforcing uniqueness.
        migrationBuilder.Sql(
            """
            WITH duplicates AS (
                SELECT
                    n."Id",
                    ROW_NUMBER() OVER (
                        PARTITION BY n."OwnerId", n."LegacyTodoItemId"
                        ORDER BY n."CreatedAtUtc", n."Id"
                    ) AS rn
                FROM "NotebookItems" n
                WHERE n."LegacyTodoItemId" IS NOT NULL
            )
            UPDATE "NotebookItems" n
            SET "LegacyTodoItemId" = NULL
            FROM duplicates d
            WHERE n."Id" = d."Id"
              AND d.rn > 1;
            """);

        migrationBuilder.DropIndex(
            name: "IX_NotebookItems_OwnerId_LegacyTodoItemId",
            table: "NotebookItems");

        migrationBuilder.CreateIndex(
            name: "IX_NotebookItems_OwnerId_LegacyTodoItemId",
            table: "NotebookItems",
            columns: new[] { "OwnerId", "LegacyTodoItemId" },
            unique: true,
            filter: "\"LegacyTodoItemId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_NotebookItems_OwnerId_IsPinned_SortOrder",
            table: "NotebookItems",
            columns: new[] { "OwnerId", "IsPinned", "SortOrder" });

        // SECTION: Mark the import completed for every current user so ordinary requests never
        // need to probe the legacy Todo table again.
        migrationBuilder.Sql(
            """
            INSERT INTO "NotebookMigrationStates" ("UserId", "MigrationKey", "CompletedAtUtc", "ImportedCount")
            SELECT
                u."Id",
                'LegacyTodoImportV1',
                CURRENT_TIMESTAMP,
                (
                    SELECT COUNT(*)::integer
                    FROM "NotebookItems" n
                    WHERE n."OwnerId" = u."Id"
                      AND n."LegacyTodoItemId" IS NOT NULL
                )
            FROM "AspNetUsers" u
            ON CONFLICT ("UserId", "MigrationKey") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data-normalisation/import operations intentionally remain intact on downgrade;
        // reversing them would risk deleting or misclassifying user-authored Notebook data.
        migrationBuilder.DropIndex(
            name: "IX_NotebookItems_OwnerId_IsPinned_SortOrder",
            table: "NotebookItems");

        migrationBuilder.DropIndex(
            name: "IX_NotebookItems_OwnerId_LegacyTodoItemId",
            table: "NotebookItems");

        migrationBuilder.CreateIndex(
            name: "IX_NotebookItems_OwnerId_LegacyTodoItemId",
            table: "NotebookItems",
            columns: new[] { "OwnerId", "LegacyTodoItemId" });

        migrationBuilder.DropTable(name: "NotebookMigrationStates");
    }
}
