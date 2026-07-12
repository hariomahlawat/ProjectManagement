using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260628090000_AddUnifiedLibraryQueryIndexes")]
public sealed class AddUnifiedLibraryQueryIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_MediaAssets_LibraryTimeline"
            ON "MediaAssets" ("IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc", "Id");

            CREATE INDEX IF NOT EXISTS "IX_MediaAssets_OriginTimeline"
            ON "MediaAssets" ("Origin", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc");

            CREATE INDEX IF NOT EXISTS "IX_MediaAssets_ProjectTimeline"
            ON "MediaAssets" ("ProjectId", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_MediaAssets_LibraryTimeline";
            DROP INDEX IF EXISTS "IX_MediaAssets_OriginTimeline";
            DROP INDEX IF EXISTS "IX_MediaAssets_ProjectTimeline";
            """);
    }
}
