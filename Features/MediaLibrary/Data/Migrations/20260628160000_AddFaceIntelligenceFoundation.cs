using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;
[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260628160000_AddFaceIntelligenceFoundation")]
public sealed class AddFaceIntelligenceFoundation : Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.Sql("""
CREATE TABLE IF NOT EXISTS "MediaFaces" (
 "Id" uuid PRIMARY KEY,"MediaAssetId" bigint NOT NULL REFERENCES "MediaAssets"("Id") ON DELETE CASCADE,"SequenceNumber" integer NOT NULL,
 "Left" double precision NOT NULL,"Top" double precision NOT NULL,"Width" double precision NOT NULL,"Height" double precision NOT NULL,"LandmarksJson" jsonb NULL,
 "DetectionConfidence" double precision NOT NULL,"QualityScore" double precision NOT NULL,"QualityStatus" varchar(32) NOT NULL,"BlurScore" double precision NULL,"BrightnessScore" double precision NULL,"PoseScore" double precision NULL,
 "IsSuppressed" boolean NOT NULL DEFAULT false,"SuppressedAtUtc" timestamptz NULL,"SuppressedByUserId" varchar(450) NULL,"DetectorModelKey" varchar(128) NOT NULL,"DetectorModelVersion" varchar(128) NOT NULL,"ReviewThumbnailPath" varchar(1024) NULL,"CreatedAtUtc" timestamptz NOT NULL,"UpdatedAtUtc" timestamptz NOT NULL,
 CONSTRAINT "AK_MediaFaces_AssetSequence" UNIQUE("MediaAssetId","SequenceNumber"));
CREATE INDEX IF NOT EXISTS "IX_MediaFaces_Quality" ON "MediaFaces"("QualityStatus","IsSuppressed");
CREATE TABLE IF NOT EXISTS "MediaFaceEmbeddings" ("Id" bigserial PRIMARY KEY,"MediaFaceId" uuid NOT NULL REFERENCES "MediaFaces"("Id") ON DELETE CASCADE,"Embedding" real[] NOT NULL,"Dimension" integer NOT NULL,"ModelKey" varchar(128) NOT NULL,"ModelVersion" varchar(128) NOT NULL,"Normalization" varchar(32) NOT NULL,"QualityScore" double precision NOT NULL,"CreatedAtUtc" timestamptz NOT NULL,"InvalidatedAtUtc" timestamptz NULL);
CREATE INDEX IF NOT EXISTS "IX_MediaFaceEmbeddings_FaceModel" ON "MediaFaceEmbeddings"("MediaFaceId","ModelKey","ModelVersion","InvalidatedAtUtc");
CREATE TABLE IF NOT EXISTS "MediaPersons" ("Id" uuid PRIMARY KEY,"DisplayName" varchar(200) NOT NULL,"NormalizedName" varchar(200) NOT NULL,"Status" varchar(32) NOT NULL,"RepresentativeFaceId" uuid NULL,"IsHidden" boolean NOT NULL DEFAULT false,"IsMinor" boolean NOT NULL DEFAULT false,"CreatedByUserId" varchar(450) NOT NULL,"CreatedAtUtc" timestamptz NOT NULL,"UpdatedAtUtc" timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS "IX_MediaPersons_Name" ON "MediaPersons"("NormalizedName");
CREATE TABLE IF NOT EXISTS "MediaPersonFaces" ("Id" bigserial PRIMARY KEY,"MediaPersonId" uuid NOT NULL REFERENCES "MediaPersons"("Id") ON DELETE CASCADE,"MediaFaceId" uuid NOT NULL REFERENCES "MediaFaces"("Id") ON DELETE CASCADE,"AssignmentType" varchar(32) NOT NULL,"AssignmentConfidence" double precision NULL,"AssignedByUserId" varchar(450) NOT NULL,"AssignedAtUtc" timestamptz NOT NULL,"RemovedAtUtc" timestamptz NULL);
CREATE INDEX IF NOT EXISTS "IX_MediaPersonFaces_PersonFace" ON "MediaPersonFaces"("MediaPersonId","MediaFaceId","RemovedAtUtc");
CREATE TABLE IF NOT EXISTS "MediaFaceReviewDecisions" ("Id" bigserial PRIMARY KEY,"MediaFaceId" uuid NOT NULL REFERENCES "MediaFaces"("Id") ON DELETE CASCADE,"CandidatePersonId" uuid NULL REFERENCES "MediaPersons"("Id") ON DELETE SET NULL,"Decision" varchar(32) NOT NULL,"Similarity" double precision NULL,"DecidedByUserId" varchar(450) NULL,"Notes" varchar(1024) NULL,"CreatedAtUtc" timestamptz NOT NULL,"DecidedAtUtc" timestamptz NULL);
CREATE INDEX IF NOT EXISTS "IX_MediaFaceReviewDecisions_Status" ON "MediaFaceReviewDecisions"("Decision","CreatedAtUtc");
CREATE TABLE IF NOT EXISTS "MediaIdentityAudits" ("Id" bigserial PRIMARY KEY,"FaceId" uuid NOT NULL,"PreviousPersonId" uuid NULL,"NewPersonId" uuid NULL,"Action" varchar(64) NOT NULL,"PerformedByUserId" varchar(450) NOT NULL,"Notes" varchar(1024) NULL,"PerformedAtUtc" timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS "IX_MediaIdentityAudits_Face" ON "MediaIdentityAudits"("FaceId","PerformedAtUtc");
""");
 }
 protected override void Down(MigrationBuilder m){m.Sql("DROP TABLE IF EXISTS \"MediaIdentityAudits\"; DROP TABLE IF EXISTS \"MediaFaceReviewDecisions\"; DROP TABLE IF EXISTS \"MediaPersonFaces\"; DROP TABLE IF EXISTS \"MediaPersons\"; DROP TABLE IF EXISTS \"MediaFaceEmbeddings\"; DROP TABLE IF EXISTS \"MediaFaces\";");}
}
