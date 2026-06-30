using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260630113000_AddIdentityReferenceGovernance")]
public sealed class AddIdentityReferenceGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE "MediaPersonFaces"
    ADD COLUMN IF NOT EXISTS "ReferenceStatus" varchar(32) NOT NULL DEFAULT 'NotReference';
ALTER TABLE "MediaPersonFaces"
    ADD COLUMN IF NOT EXISTS "ReferenceChangedByUserId" varchar(450) NULL;
ALTER TABLE "MediaPersonFaces"
    ADD COLUMN IF NOT EXISTS "ReferenceChangedAtUtc" timestamptz NULL;
ALTER TABLE "MediaPersonFaces"
    ADD COLUMN IF NOT EXISTS "ReferenceChangeReason" varchar(1024) NULL;

CREATE INDEX IF NOT EXISTS "IX_MediaPersonFaces_TrustedReferences"
ON "MediaPersonFaces"("MediaPersonId", "ReferenceStatus", "RemovedAtUtc");

ALTER TABLE "MediaFaceReviewDecisions"
    ADD COLUMN IF NOT EXISTS "BestReferenceSimilarity" double precision NULL;
ALTER TABLE "MediaFaceReviewDecisions"
    ADD COLUMN IF NOT EXISTS "MeanTopSimilarity" double precision NULL;
ALTER TABLE "MediaFaceReviewDecisions"
    ADD COLUMN IF NOT EXISTS "ReferenceCount" integer NOT NULL DEFAULT 0;
ALTER TABLE "MediaFaceReviewDecisions"
    ADD COLUMN IF NOT EXISTS "MarginToNext" double precision NULL;
ALTER TABLE "MediaFaceReviewDecisions"
    ADD COLUMN IF NOT EXISTS "MarginAvailable" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE "MediaFaceReviewDecisions"
    ADD COLUMN IF NOT EXISTS "ConfidenceLevel" varchar(32) NOT NULL DEFAULT 'None';

-- Missing source files are an availability outcome, not a worker defect. Convert
-- historical dead-letter rows so administration no longer reports them as active failures.
UPDATE "MediaProcessingJobs"
SET "Status" = 'Completed',
    "CompletedAtUtc" = COALESCE("CompletedAtUtc", CURRENT_TIMESTAMP),
    "FailureCode" = 'SourceUnavailable',
    "LockedBy" = NULL,
    "LockExpiresAtUtc" = NULL,
    "UpdatedAtUtc" = CURRENT_TIMESTAMP
WHERE "Status" IN ('Failed', 'DeadLetter')
  AND "FailureCode" IN ('MediaContentUnavailableException', 'FileNotFoundException', 'DirectoryNotFoundException');

-- Preserve the safest existing reference for every person: the explicitly selected
-- representative face. Other confirmed appearances remain confirmed but are deliberately
-- excluded from matching until a reviewer promotes them.
UPDATE "MediaPersonFaces" assignment
SET "ReferenceStatus" = 'TrustedReference',
    "ReferenceChangedByUserId" = 'migration',
    "ReferenceChangedAtUtc" = CURRENT_TIMESTAMP,
    "ReferenceChangeReason" = 'Existing representative face trusted during reference-governance migration.'
FROM "MediaPersons" person,
     "MediaFaces" face,
     "MediaAssets" asset
WHERE assignment."MediaPersonId" = person."Id"
  AND assignment."MediaFaceId" = person."RepresentativeFaceId"
  AND assignment."MediaFaceId" = face."Id"
  AND face."MediaAssetId" = asset."Id"
  AND assignment."RemovedAtUtc" IS NULL
  AND face."IsSuppressed" = FALSE
  AND face."QualityStatus" = 'EmbeddingEligible'
  AND face."QualityScore" >= 0.65
  AND asset."IsAvailable" = TRUE
  AND asset."IsDeleted" = FALSE
  AND asset."IsArchived" = FALSE
  AND EXISTS (
      SELECT 1
      FROM "MediaFaceEmbeddings" embedding
      WHERE embedding."MediaFaceId" = face."Id"
        AND embedding."InvalidatedAtUtc" IS NULL);

-- Discard only disposable pending model suggestions generated under the previous permissive
-- reference policy. Human decisions and confirmed assignments remain intact and audited.
DELETE FROM "MediaFaceReviewDecisions"
WHERE "Decision" = 'Pending'
  AND "CandidatePersonId" IS NOT NULL;

-- Requeue valid unassigned faces so suggestions are regenerated from trusted references and
-- the corrected open-set evidence policy.
UPDATE "MediaFaces" face
SET "CandidateSearchStatus" = 'Pending',
    "CandidateSearchFailureReason" = NULL,
    "CandidateSearchCompletedAtUtc" = NULL,
    "CandidateSearchModelKey" = NULL,
    "CandidateSearchModelVersion" = NULL,
    "UpdatedAtUtc" = CURRENT_TIMESTAMP
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
DROP INDEX IF EXISTS "IX_MediaPersonFaces_TrustedReferences";

ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "ConfidenceLevel";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "MarginAvailable";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "MarginToNext";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "ReferenceCount";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "MeanTopSimilarity";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "BestReferenceSimilarity";

ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "ReferenceChangeReason";
ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "ReferenceChangedAtUtc";
ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "ReferenceChangedByUserId";
ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "ReferenceStatus";
""");
    }
}
