using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddTotColumnsIfMissing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocuments"
                    ADD COLUMN IF NOT EXISTS "TotId" integer;
                """);
                migrationBuilder.Sql("""
                    CREATE INDEX IF NOT EXISTS "IX_ProjectDocuments_TotId"
                    ON "ProjectDocuments" ("TotId");
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocumentRequests"
                    ADD COLUMN IF NOT EXISTS "TotId" integer;
                """);
                migrationBuilder.Sql("""
                    CREATE INDEX IF NOT EXISTS "IX_ProjectDocumentRequests_TotId"
                    ON "ProjectDocumentRequests" ("TotId");
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectPhotos"
                    ADD COLUMN IF NOT EXISTS "TotId" integer;
                """);
                migrationBuilder.Sql("""
                    CREATE INDEX IF NOT EXISTS "IX_ProjectPhotos_TotId"
                    ON "ProjectPhotos" ("TotId");
                """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    IF COL_LENGTH('ProjectDocuments', 'TotId') IS NULL
                    BEGIN
                        ALTER TABLE [ProjectDocuments] ADD [TotId] int NULL;
                    END
                """);
                migrationBuilder.Sql("""
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectDocuments_TotId' AND object_id = OBJECT_ID('[dbo].[ProjectDocuments]'))
                    BEGIN
                        CREATE INDEX [IX_ProjectDocuments_TotId] ON [ProjectDocuments] ([TotId]);
                    END
                """);
                migrationBuilder.Sql("""
                    IF COL_LENGTH('ProjectDocumentRequests', 'TotId') IS NULL
                    BEGIN
                        ALTER TABLE [ProjectDocumentRequests] ADD [TotId] int NULL;
                    END
                """);
                migrationBuilder.Sql("""
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectDocumentRequests_TotId' AND object_id = OBJECT_ID('[dbo].[ProjectDocumentRequests]'))
                    BEGIN
                        CREATE INDEX [IX_ProjectDocumentRequests_TotId] ON [ProjectDocumentRequests] ([TotId]);
                    END
                """);
                migrationBuilder.Sql("""
                    IF COL_LENGTH('ProjectPhotos', 'TotId') IS NULL
                    BEGIN
                        ALTER TABLE [ProjectPhotos] ADD [TotId] int NULL;
                    END
                """);
                migrationBuilder.Sql("""
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectPhotos_TotId' AND object_id = OBJECT_ID('[dbo].[ProjectPhotos]'))
                    BEGIN
                        CREATE INDEX [IX_ProjectPhotos_TotId] ON [ProjectPhotos] ([TotId]);
                    END
                """);
            }
            else
            {
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocuments" ADD COLUMN IF NOT EXISTS "TotId" INTEGER;
                """);
                migrationBuilder.Sql("""
                    CREATE INDEX IF NOT EXISTS "IX_ProjectDocuments_TotId"
                    ON "ProjectDocuments" ("TotId");
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocumentRequests" ADD COLUMN IF NOT EXISTS "TotId" INTEGER;
                """);
                migrationBuilder.Sql("""
                    CREATE INDEX IF NOT EXISTS "IX_ProjectDocumentRequests_TotId"
                    ON "ProjectDocumentRequests" ("TotId");
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectPhotos" ADD COLUMN IF NOT EXISTS "TotId" INTEGER;
                """);
                migrationBuilder.Sql("""
                    CREATE INDEX IF NOT EXISTS "IX_ProjectPhotos_TotId"
                    ON "ProjectPhotos" ("TotId");
                """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_ProjectPhotos_TotId";
                """);
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_ProjectDocumentRequests_TotId";
                """);
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_ProjectDocuments_TotId";
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectPhotos" DROP COLUMN IF EXISTS "TotId";
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocumentRequests" DROP COLUMN IF EXISTS "TotId";
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocuments" DROP COLUMN IF EXISTS "TotId";
                """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectPhotos_TotId' AND object_id = OBJECT_ID('[dbo].[ProjectPhotos]'))
                    BEGIN
                        DROP INDEX [IX_ProjectPhotos_TotId] ON [ProjectPhotos];
                    END
                """);
                migrationBuilder.Sql("""
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectDocumentRequests_TotId' AND object_id = OBJECT_ID('[dbo].[ProjectDocumentRequests]'))
                    BEGIN
                        DROP INDEX [IX_ProjectDocumentRequests_TotId] ON [ProjectDocumentRequests];
                    END
                """);
                migrationBuilder.Sql("""
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProjectDocuments_TotId' AND object_id = OBJECT_ID('[dbo].[ProjectDocuments]'))
                    BEGIN
                        DROP INDEX [IX_ProjectDocuments_TotId] ON [ProjectDocuments];
                    END
                """);
                migrationBuilder.Sql("""
                    IF COL_LENGTH('ProjectPhotos', 'TotId') IS NOT NULL
                    BEGIN
                        ALTER TABLE [ProjectPhotos] DROP COLUMN [TotId];
                    END
                """);
                migrationBuilder.Sql("""
                    IF COL_LENGTH('ProjectDocumentRequests', 'TotId') IS NOT NULL
                    BEGIN
                        ALTER TABLE [ProjectDocumentRequests] DROP COLUMN [TotId];
                    END
                """);
                migrationBuilder.Sql("""
                    IF COL_LENGTH('ProjectDocuments', 'TotId') IS NOT NULL
                    BEGIN
                        ALTER TABLE [ProjectDocuments] DROP COLUMN [TotId];
                    END
                """);
            }
            else
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_ProjectPhotos_TotId";
                """);
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_ProjectDocumentRequests_TotId";
                """);
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_ProjectDocuments_TotId";
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectPhotos" DROP COLUMN IF EXISTS "TotId";
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocumentRequests" DROP COLUMN IF EXISTS "TotId";
                """);
                migrationBuilder.Sql("""
                    ALTER TABLE "ProjectDocuments" DROP COLUMN IF EXISTS "TotId";
                """);
            }
        }
    }
}
