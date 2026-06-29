using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260629170000_AddIncrementalKnownPersonMatching")]
public sealed class AddIncrementalKnownPersonMatching : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "CandidateSearchStatus" varchar(32) NOT NULL DEFAULT 'NotRequested';
ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "CandidateSearchModelKey" varchar(128) NULL;
ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "CandidateSearchModelVersion" varchar(128) NULL;
ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "CandidateSearchFailureReason" varchar(2048) NULL;
ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "CandidateSearchCompletedAtUtc" timestamptz NULL;

CREATE INDEX IF NOT EXISTS "IX_MediaFaces_CandidateSearchQueue"
ON "MediaFaces"("CandidateSearchStatus", "CandidateSearchModelKey", "CandidateSearchModelVersion", "UpdatedAtUtc");

-- Existing valid, unassigned embeddings are queued for the active application model on
-- first worker pass. The model key/version remain null here so configuration remains the
-- authoritative source and the migration is deployment-neutral.
UPDATE "MediaFaces" face
SET "CandidateSearchStatus" = 'Pending',
    "CandidateSearchFailureReason" = NULL,
    "CandidateSearchCompletedAtUtc" = NULL
WHERE face."IsSuppressed" = FALSE
  AND face."QualityStatus" = 'EmbeddingEligible'
  AND NOT EXISTS (
      SELECT 1
      FROM "MediaPersonFaces" assignment
      WHERE assignment."MediaFaceId" = face."Id"
        AND assignment."RemovedAtUtc" IS NULL)
  AND EXISTS (
      SELECT 1
      FROM "MediaFaceEmbeddings" embedding
      WHERE embedding."MediaFaceId" = face."Id"
        AND embedding."InvalidatedAtUtc" IS NULL)
  AND NOT EXISTS (
      SELECT 1
      FROM "MediaFaceReviewDecisions" decision
      WHERE decision."MediaFaceId" = face."Id"
        AND decision."CandidatePersonId" IS NULL
        AND decision."Decision" = 'Ignored');
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP INDEX IF EXISTS "IX_MediaFaces_CandidateSearchQueue";
ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "CandidateSearchCompletedAtUtc";
ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "CandidateSearchFailureReason";
ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "CandidateSearchModelVersion";
ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "CandidateSearchModelKey";
ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "CandidateSearchStatus";
""");
    }
}
