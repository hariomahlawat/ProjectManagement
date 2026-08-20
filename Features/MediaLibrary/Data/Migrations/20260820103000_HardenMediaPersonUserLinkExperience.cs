using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260820103000_HardenMediaPersonUserLinkExperience")]
public sealed class HardenMediaPersonUserLinkExperience : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE "MediaPersonUserLinks"
    ADD COLUMN IF NOT EXISTS "UsePortraitAsAvatar" boolean NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS "ConcernRaisedAtUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "ConcernRaisedByUserId" varchar(450) NULL,
    ADD COLUMN IF NOT EXISTS "ConcernReason" varchar(1024) NULL,
    ADD COLUMN IF NOT EXISTS "ConcernResolvedAtUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "ConcernResolvedByUserId" varchar(450) NULL,
    ADD COLUMN IF NOT EXISTS "ConcernResolution" varchar(1024) NULL;

CREATE INDEX IF NOT EXISTS "IX_MediaPersonUserLinks_OpenConcern"
ON "MediaPersonUserLinks" ("MediaPersonId")
WHERE "UnlinkedAtUtc" IS NULL
  AND "ConcernRaisedAtUtc" IS NOT NULL
  AND "ConcernResolvedAtUtc" IS NULL;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP INDEX IF EXISTS "IX_MediaPersonUserLinks_OpenConcern";

ALTER TABLE "MediaPersonUserLinks"
    DROP COLUMN IF EXISTS "ConcernResolution",
    DROP COLUMN IF EXISTS "ConcernResolvedByUserId",
    DROP COLUMN IF EXISTS "ConcernResolvedAtUtc",
    DROP COLUMN IF EXISTS "ConcernReason",
    DROP COLUMN IF EXISTS "ConcernRaisedByUserId",
    DROP COLUMN IF EXISTS "ConcernRaisedAtUtc",
    DROP COLUMN IF EXISTS "UsePortraitAsAvatar";
""");
    }
}
