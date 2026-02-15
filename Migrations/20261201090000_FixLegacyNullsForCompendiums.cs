using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201090000_FixLegacyNullsForCompendiums")]
    public partial class FixLegacyNullsForCompendiums : Migration
    {
        // SECTION: Repair legacy NULL values and enforce Projects schema contract
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $$
BEGIN
  -- Backfill nullable legacy values before tightening constraints
  UPDATE "Projects" SET "IsDeleted" = FALSE WHERE "IsDeleted" IS NULL;
  UPDATE "Projects" SET "IsArchived" = FALSE WHERE "IsArchived" IS NULL;
  UPDATE "Projects" SET "IsBuild" = FALSE WHERE "IsBuild" IS NULL;

  UPDATE "Projects" SET "CoverPhotoVersion" = 1 WHERE "CoverPhotoVersion" IS NULL;
  UPDATE "Projects" SET "FeaturedVideoVersion" = 1 WHERE "FeaturedVideoVersion" IS NULL;

  -- LifecycleStatus uses string conversion in PostgreSQL
  UPDATE "Projects" SET "LifecycleStatus" = 'Active' WHERE "LifecycleStatus" IS NULL OR "LifecycleStatus" = '';

  -- Enforce defaults for all non-nullable value columns
  ALTER TABLE "Projects" ALTER COLUMN "IsDeleted" SET DEFAULT FALSE;
  ALTER TABLE "Projects" ALTER COLUMN "IsArchived" SET DEFAULT FALSE;
  ALTER TABLE "Projects" ALTER COLUMN "IsBuild" SET DEFAULT FALSE;
  ALTER TABLE "Projects" ALTER COLUMN "CoverPhotoVersion" SET DEFAULT 1;
  ALTER TABLE "Projects" ALTER COLUMN "FeaturedVideoVersion" SET DEFAULT 1;
  ALTER TABLE "Projects" ALTER COLUMN "LifecycleStatus" SET DEFAULT 'Active';

  -- Reassert not-null contract expected by current EF model
  ALTER TABLE "Projects" ALTER COLUMN "IsDeleted" SET NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "IsArchived" SET NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "IsBuild" SET NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "CoverPhotoVersion" SET NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "FeaturedVideoVersion" SET NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "LifecycleStatus" SET NOT NULL;
END
$$;
""");

            // SECTION: Repair ProjectTechStatuses proliferation availability contract
            migrationBuilder.Sql("""
DO $$
BEGIN
  UPDATE "ProjectTechStatuses"
  SET "AvailableForProliferation" = FALSE
  WHERE "AvailableForProliferation" IS NULL;

  ALTER TABLE "ProjectTechStatuses"
    ALTER COLUMN "AvailableForProliferation" SET DEFAULT FALSE;

  ALTER TABLE "ProjectTechStatuses"
    ALTER COLUMN "AvailableForProliferation" SET NOT NULL;
END
$$;
""");

            // SECTION: Repair ProjectTots status contract
            migrationBuilder.Sql("""
DO $$
BEGIN
  UPDATE "ProjectTots"
  SET "Status" = 'NotStarted'
  WHERE "Status" IS NULL OR "Status" = '';

  ALTER TABLE "ProjectTots"
    ALTER COLUMN "Status" SET DEFAULT 'NotStarted';

  ALTER TABLE "ProjectTots"
    ALTER COLUMN "Status" SET NOT NULL;
END
$$;
""");
        }

        // SECTION: Revert explicit defaults/not-null constraints introduced by this guard migration
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $$
BEGIN
  ALTER TABLE "ProjectTots" ALTER COLUMN "Status" DROP NOT NULL;
  ALTER TABLE "ProjectTots" ALTER COLUMN "Status" DROP DEFAULT;

  ALTER TABLE "ProjectTechStatuses" ALTER COLUMN "AvailableForProliferation" DROP NOT NULL;
  ALTER TABLE "ProjectTechStatuses" ALTER COLUMN "AvailableForProliferation" DROP DEFAULT;

  ALTER TABLE "Projects" ALTER COLUMN "LifecycleStatus" DROP NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "LifecycleStatus" DROP DEFAULT;

  ALTER TABLE "Projects" ALTER COLUMN "FeaturedVideoVersion" DROP NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "FeaturedVideoVersion" DROP DEFAULT;

  ALTER TABLE "Projects" ALTER COLUMN "CoverPhotoVersion" DROP NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "CoverPhotoVersion" DROP DEFAULT;

  ALTER TABLE "Projects" ALTER COLUMN "IsBuild" DROP NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "IsBuild" DROP DEFAULT;

  ALTER TABLE "Projects" ALTER COLUMN "IsArchived" DROP NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "IsArchived" DROP DEFAULT;

  ALTER TABLE "Projects" ALTER COLUMN "IsDeleted" DROP NOT NULL;
  ALTER TABLE "Projects" ALTER COLUMN "IsDeleted" DROP DEFAULT;
END
$$;
""");
        }
    }
}
