using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260628190000_HardenPeopleExperience")]
public sealed class HardenPeopleExperience : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE "MediaAssets" ADD COLUMN IF NOT EXISTS "FaceAnalysisStatus" varchar(32) NOT NULL DEFAULT 'NotRequested';
ALTER TABLE "MediaAssets" ADD COLUMN IF NOT EXISTS "FaceAnalysisVersion" varchar(256) NULL;
ALTER TABLE "MediaAssets" ADD COLUMN IF NOT EXISTS "FaceAnalysedAtUtc" timestamptz NULL;
ALTER TABLE "MediaAssets" ADD COLUMN IF NOT EXISTS "FaceProcessingFailureReason" varchar(2048) NULL;
CREATE INDEX IF NOT EXISTS "IX_MediaAssets_FaceAnalysis"
ON "MediaAssets"("FaceAnalysisStatus", "FaceAnalysisVersion");

ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "QualitySignalsJson" jsonb NULL;
ALTER TABLE "MediaFaces" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" uuid NULL;
UPDATE "MediaFaces"
SET "ConcurrencyToken" = (md5(random()::text || clock_timestamp()::text || "Id"::text))::uuid
WHERE "ConcurrencyToken" IS NULL;
ALTER TABLE "MediaFaces" ALTER COLUMN "ConcurrencyToken" SET NOT NULL;
ALTER TABLE "MediaFaces" ALTER COLUMN "ConcurrencyToken" SET DEFAULT (md5(random()::text || clock_timestamp()::text))::uuid;

ALTER TABLE "MediaPersons" ADD COLUMN IF NOT EXISTS "MergedIntoPersonId" uuid NULL;
ALTER TABLE "MediaPersons" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" uuid NULL;
UPDATE "MediaPersons"
SET "ConcurrencyToken" = (md5(random()::text || clock_timestamp()::text || "Id"::text))::uuid
WHERE "ConcurrencyToken" IS NULL;
ALTER TABLE "MediaPersons" ALTER COLUMN "ConcurrencyToken" SET NOT NULL;
ALTER TABLE "MediaPersons" ALTER COLUMN "ConcurrencyToken" SET DEFAULT (md5(random()::text || clock_timestamp()::text))::uuid;
CREATE INDEX IF NOT EXISTS "IX_MediaPersons_MergedIntoPersonId" ON "MediaPersons"("MergedIntoPersonId");

ALTER TABLE "MediaPersonFaces" ADD COLUMN IF NOT EXISTS "RemovedByUserId" varchar(450) NULL;
ALTER TABLE "MediaPersonFaces" ADD COLUMN IF NOT EXISTS "RemovalReason" varchar(1024) NULL;
ALTER TABLE "MediaPersonFaces" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" uuid NULL;
UPDATE "MediaPersonFaces"
SET "ConcurrencyToken" = (md5(random()::text || clock_timestamp()::text || "Id"::text))::uuid
WHERE "ConcurrencyToken" IS NULL;
ALTER TABLE "MediaPersonFaces" ALTER COLUMN "ConcurrencyToken" SET NOT NULL;
ALTER TABLE "MediaPersonFaces" ALTER COLUMN "ConcurrencyToken" SET DEFAULT (md5(random()::text || clock_timestamp()::text))::uuid;

WITH ranked AS (
    SELECT "Id",
           row_number() OVER (PARTITION BY "MediaFaceId" ORDER BY "AssignedAtUtc" DESC, "Id" DESC) AS rn
    FROM "MediaPersonFaces"
    WHERE "RemovedAtUtc" IS NULL
)
UPDATE "MediaPersonFaces" assignment
SET "RemovedAtUtc" = clock_timestamp(),
    "RemovalReason" = 'Automatically closed while enforcing one active assignment per face.'
FROM ranked
WHERE assignment."Id" = ranked."Id" AND ranked.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_MediaPersonFaces_OneActiveAssignmentPerFace"
ON "MediaPersonFaces"("MediaFaceId")
WHERE "RemovedAtUtc" IS NULL;
CREATE INDEX IF NOT EXISTS "IX_MediaPersonFaces_ActivePersonTimeline"
ON "MediaPersonFaces"("MediaPersonId", "RemovedAtUtc", "AssignedAtUtc");

ALTER TABLE "MediaFaceReviewDecisions" ADD COLUMN IF NOT EXISTS "ModelKey" varchar(128) NOT NULL DEFAULT '';
ALTER TABLE "MediaFaceReviewDecisions" ADD COLUMN IF NOT EXISTS "ModelVersion" varchar(128) NOT NULL DEFAULT '';
ALTER TABLE "MediaFaceReviewDecisions" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" uuid NULL;
UPDATE "MediaFaceReviewDecisions"
SET "ConcurrencyToken" = (md5(random()::text || clock_timestamp()::text || "Id"::text))::uuid
WHERE "ConcurrencyToken" IS NULL;
ALTER TABLE "MediaFaceReviewDecisions" ALTER COLUMN "ConcurrencyToken" SET NOT NULL;
ALTER TABLE "MediaFaceReviewDecisions" ALTER COLUMN "ConcurrencyToken" SET DEFAULT (md5(random()::text || clock_timestamp()::text))::uuid;

UPDATE "MediaFaceReviewDecisions" decision
SET "ModelKey" = COALESCE((
        SELECT item."ModelKey"
        FROM "MediaFaceEmbeddings" item
        WHERE item."MediaFaceId" = decision."MediaFaceId"
          AND item."InvalidatedAtUtc" IS NULL
        ORDER BY item."CreatedAtUtc" DESC
        LIMIT 1
    ), ''),
    "ModelVersion" = COALESCE((
        SELECT item."ModelVersion"
        FROM "MediaFaceEmbeddings" item
        WHERE item."MediaFaceId" = decision."MediaFaceId"
          AND item."InvalidatedAtUtc" IS NULL
        ORDER BY item."CreatedAtUtc" DESC
        LIMIT 1
    ), '')
WHERE decision."ModelKey" = '';

WITH ranked AS (
    SELECT "Id",
           row_number() OVER (
               PARTITION BY "MediaFaceId", "CandidatePersonId"
               ORDER BY "Similarity" DESC NULLS LAST, "CreatedAtUtc", "Id") AS rn
    FROM "MediaFaceReviewDecisions"
    WHERE "Decision" = 'Pending' AND "CandidatePersonId" IS NOT NULL
)
UPDATE "MediaFaceReviewDecisions" decision
SET "Decision" = 'Ignored',
    "Notes" = 'Duplicate pending candidate closed during schema hardening.',
    "DecidedAtUtc" = clock_timestamp()
FROM ranked
WHERE decision."Id" = ranked."Id" AND ranked.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_MediaFaceReviewDecisions_PendingCandidate"
ON "MediaFaceReviewDecisions"("MediaFaceId", "CandidatePersonId")
WHERE "Decision" = 'Pending' AND "CandidatePersonId" IS NOT NULL;

WITH ranked AS (
    SELECT "Id",
           row_number() OVER (PARTITION BY "MediaFaceId" ORDER BY "CreatedAtUtc", "Id") AS rn
    FROM "MediaFaceReviewDecisions"
    WHERE "Decision" = 'Ignored' AND "CandidatePersonId" IS NULL
)
DELETE FROM "MediaFaceReviewDecisions" decision
USING ranked
WHERE decision."Id" = ranked."Id" AND ranked.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_MediaFaceReviewDecisions_IgnoredFace"
ON "MediaFaceReviewDecisions"("MediaFaceId")
WHERE "Decision" = 'Ignored' AND "CandidatePersonId" IS NULL;
CREATE INDEX IF NOT EXISTS "IX_MediaFaceReviewDecisions_ModelDecision"
ON "MediaFaceReviewDecisions"("MediaFaceId", "ModelKey", "ModelVersion", "Decision");
CREATE INDEX IF NOT EXISTS "IX_MediaFaceEmbeddings_CandidateLookup"
ON "MediaFaceEmbeddings"("ModelKey", "ModelVersion", "Dimension", "InvalidatedAtUtc", "QualityScore");

ALTER TABLE "MediaIdentityAudits" ALTER COLUMN "FaceId" DROP NOT NULL;
ALTER TABLE "MediaIdentityAudits" ADD COLUMN IF NOT EXISTS "PersonId" uuid NULL;
ALTER TABLE "MediaIdentityAudits" ADD COLUMN IF NOT EXISTS "MetadataJson" jsonb NULL;
CREATE INDEX IF NOT EXISTS "IX_MediaIdentityAudits_Person"
ON "MediaIdentityAudits"("PersonId", "PerformedAtUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP INDEX IF EXISTS "IX_MediaIdentityAudits_Person";
DELETE FROM "MediaIdentityAudits" WHERE "FaceId" IS NULL;
ALTER TABLE "MediaIdentityAudits" DROP COLUMN IF EXISTS "MetadataJson";
ALTER TABLE "MediaIdentityAudits" DROP COLUMN IF EXISTS "PersonId";
ALTER TABLE "MediaIdentityAudits" ALTER COLUMN "FaceId" SET NOT NULL;

DROP INDEX IF EXISTS "IX_MediaFaceEmbeddings_CandidateLookup";
DROP INDEX IF EXISTS "IX_MediaFaceReviewDecisions_ModelDecision";
DROP INDEX IF EXISTS "UX_MediaFaceReviewDecisions_IgnoredFace";
DROP INDEX IF EXISTS "UX_MediaFaceReviewDecisions_PendingCandidate";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "ConcurrencyToken";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "ModelVersion";
ALTER TABLE "MediaFaceReviewDecisions" DROP COLUMN IF EXISTS "ModelKey";

DROP INDEX IF EXISTS "IX_MediaPersonFaces_ActivePersonTimeline";
DROP INDEX IF EXISTS "UX_MediaPersonFaces_OneActiveAssignmentPerFace";
ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "ConcurrencyToken";
ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "RemovalReason";
ALTER TABLE "MediaPersonFaces" DROP COLUMN IF EXISTS "RemovedByUserId";

DROP INDEX IF EXISTS "IX_MediaPersons_MergedIntoPersonId";
ALTER TABLE "MediaPersons" DROP COLUMN IF EXISTS "ConcurrencyToken";
ALTER TABLE "MediaPersons" DROP COLUMN IF EXISTS "MergedIntoPersonId";

ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "ConcurrencyToken";
ALTER TABLE "MediaFaces" DROP COLUMN IF EXISTS "QualitySignalsJson";

DROP INDEX IF EXISTS "IX_MediaAssets_FaceAnalysis";
ALTER TABLE "MediaAssets" DROP COLUMN IF EXISTS "FaceProcessingFailureReason";
ALTER TABLE "MediaAssets" DROP COLUMN IF EXISTS "FaceAnalysedAtUtc";
ALTER TABLE "MediaAssets" DROP COLUMN IF EXISTS "FaceAnalysisVersion";
ALTER TABLE "MediaAssets" DROP COLUMN IF EXISTS "FaceAnalysisStatus";
""");
    }
}
