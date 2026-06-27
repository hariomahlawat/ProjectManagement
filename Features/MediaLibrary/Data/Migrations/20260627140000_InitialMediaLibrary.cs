using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260627140000_InitialMediaLibrary")]
public sealed class InitialMediaLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

        migrationBuilder.CreateTable(
            name: "MediaLibrarySources",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                RootPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                IsReadOnly = table.Column<bool>(type: "boolean", nullable: false),
                IncludeSubfolders = table.Column<bool>(type: "boolean", nullable: false),
                AllowedExtensionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                ConfigurationFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                LastScanStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastScanCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastSuccessfulScanAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ScanRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ScanStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Never"),
                LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                IndexedAssetCount = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MediaLibrarySources", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MediaPeople",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LinkedUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Organisation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                RepresentativeFaceId = table.Column<Guid>(type: "uuid", nullable: true),
                IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                IsMinor = table.Column<bool>(type: "boolean", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MediaPeople", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MediaAssets",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                Origin = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                SourceEntityId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                ParentEntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                RelativePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                FileModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                QuickFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ContextKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                CollectionKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                ContextTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                ContextSubtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SourceLabel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Caption = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                MediaDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IndexedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenScanId = table.Column<Guid>(type: "uuid", nullable: false),
                Width = table.Column<int>(type: "integer", nullable: true),
                Height = table.Column<int>(type: "integer", nullable: true),
                DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                VersionToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                IsCover = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<long>(type: "bigint", nullable: false),
                IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ClassificationConfidence = table.Column<double>(type: "double precision", nullable: true),
                DerivativeStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AnalysisStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AnalysisVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                AnalysisSignalsJson = table.Column<string>(type: "jsonb", nullable: true),
                AnalysedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ProcessingFailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CacheVersion = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaAssets", x => x.Id);
                table.ForeignKey("FK_MediaAssets_MediaLibrarySources_SourceId", x => x.SourceId, "MediaLibrarySources", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MediaFaceClusters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                RepresentativeFaceId = table.Column<Guid>(type: "uuid", nullable: true),
                FaceCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaFaceClusters", x => x.Id);
                table.ForeignKey("FK_MediaFaceClusters_MediaPeople_PersonId", x => x.PersonId, "MediaPeople", "Id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "MediaProcessingJobs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                JobType = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                AvailableAfterUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                LockExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                FailureMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaProcessingJobs", x => x.Id);
                table.ForeignKey("FK_MediaProcessingJobs_MediaAssets_MediaAssetId", x => x.MediaAssetId, "MediaAssets", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MediaFaces",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                Left = table.Column<double>(type: "double precision", nullable: false),
                Top = table.Column<double>(type: "double precision", nullable: false),
                Width = table.Column<double>(type: "double precision", nullable: false),
                Height = table.Column<double>(type: "double precision", nullable: false),
                DetectionConfidence = table.Column<double>(type: "double precision", nullable: false),
                QualityScore = table.Column<double>(type: "double precision", nullable: false),
                Embedding = table.Column<Vector>(type: "vector(512)", nullable: true),
                PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                FaceClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                IdentityStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                MatchConfidence = table.Column<double>(type: "double precision", nullable: true),
                IsManuallyConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                DetectorModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EmbeddingModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaFaces", x => x.Id);
                table.ForeignKey("FK_MediaFaces_MediaAssets_MediaAssetId", x => x.MediaAssetId, "MediaAssets", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_MediaFaces_MediaFaceClusters_FaceClusterId", x => x.FaceClusterId, "MediaFaceClusters", "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_MediaFaces_MediaPeople_PersonId", x => x.PersonId, "MediaPeople", "Id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "MediaIdentityAudits",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FaceId = table.Column<Guid>(type: "uuid", nullable: false),
                PreviousPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                NewPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                PerformedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MediaIdentityAudits", x => x.Id));

        migrationBuilder.CreateIndex("IX_MediaLibrarySources_Key", "MediaLibrarySources", "Key", unique: true);
        migrationBuilder.CreateIndex("IX_MediaLibrarySources_IsEnabled_SourceType", "MediaLibrarySources", new[] { "IsEnabled", "SourceType" });
        migrationBuilder.CreateIndex("IX_MediaAssets_SourceId_SourceEntityId", "MediaAssets", new[] { "SourceId", "SourceEntityId" }, unique: true);
        migrationBuilder.CreateIndex("IX_MediaAssets_IsAvailable_IsDeleted_MediaDateUtc", "MediaAssets", new[] { "IsAvailable", "IsDeleted", "MediaDateUtc" });
        migrationBuilder.CreateIndex("IX_MediaAssets_Kind_Classification", "MediaAssets", new[] { "Kind", "Classification" });
        migrationBuilder.CreateIndex("IX_MediaAssets_ProjectId", "MediaAssets", "ProjectId");
        migrationBuilder.CreateIndex("IX_MediaAssets_CollectionKey", "MediaAssets", "CollectionKey");
        migrationBuilder.CreateIndex("IX_MediaProcessingJobs_MediaAssetId_JobType", "MediaProcessingJobs", new[] { "MediaAssetId", "JobType" }, unique: true);
        migrationBuilder.CreateIndex("IX_MediaProcessingJobs_Status_AvailableAfterUtc", "MediaProcessingJobs", new[] { "Status", "AvailableAfterUtc" });
        migrationBuilder.CreateIndex("IX_MediaPeople_NormalizedName", "MediaPeople", "NormalizedName");
        migrationBuilder.CreateIndex("IX_MediaPeople_LinkedUserId", "MediaPeople", "LinkedUserId");
        migrationBuilder.CreateIndex("IX_MediaFaceClusters_PersonId", "MediaFaceClusters", "PersonId");
        migrationBuilder.CreateIndex("IX_MediaFaces_MediaAssetId", "MediaFaces", "MediaAssetId");
        migrationBuilder.CreateIndex("IX_MediaFaces_PersonId", "MediaFaces", "PersonId");
        migrationBuilder.CreateIndex("IX_MediaFaces_FaceClusterId", "MediaFaces", "FaceClusterId");
        migrationBuilder.CreateIndex("IX_MediaIdentityAudits_FaceId_PerformedAtUtc", "MediaIdentityAudits", new[] { "FaceId", "PerformedAtUtc" });

        migrationBuilder.Sql(@"
            CREATE INDEX IF NOT EXISTS ""IX_MediaFaces_Embedding_Hnsw""
            ON ""MediaFaces""
            USING hnsw (""Embedding"" vector_cosine_ops)
            WHERE ""Embedding"" IS NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MediaIdentityAudits");
        migrationBuilder.DropTable("MediaProcessingJobs");
        migrationBuilder.DropTable("MediaFaces");
        migrationBuilder.DropTable("MediaAssets");
        migrationBuilder.DropTable("MediaFaceClusters");
        migrationBuilder.DropTable("MediaLibrarySources");
        migrationBuilder.DropTable("MediaPeople");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.19");
        MediaLibraryModelConfiguration.Configure(modelBuilder);
    }
}
