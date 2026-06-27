using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260627140000_InitialMediaLibrary")]
public sealed class InitialMediaLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Core catalogue only. Face/People tables and pgvector are intentionally deferred
        // to a separate opt-in release.
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
                table.ForeignKey(
                    name: "FK_MediaAssets_MediaLibrarySources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "MediaLibrarySources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
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
                table.ForeignKey(
                    name: "FK_MediaProcessingJobs_MediaAssets_MediaAssetId",
                    column: x => x.MediaAssetId,
                    principalTable: "MediaAssets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_MediaLibrarySources_Key", "MediaLibrarySources", "Key", unique: true);
        migrationBuilder.CreateIndex("IX_MediaLibrarySources_IsEnabled_SourceType", "MediaLibrarySources", new[] { "IsEnabled", "SourceType" });
        migrationBuilder.CreateIndex("IX_MediaAssets_SourceId_SourceEntityId", "MediaAssets", new[] { "SourceId", "SourceEntityId" }, unique: true);
        migrationBuilder.CreateIndex("IX_MediaAssets_IsAvailable_IsDeleted_MediaDateUtc", "MediaAssets", new[] { "IsAvailable", "IsDeleted", "MediaDateUtc" });
        migrationBuilder.CreateIndex("IX_MediaAssets_Kind_Classification", "MediaAssets", new[] { "Kind", "Classification" });
        migrationBuilder.CreateIndex("IX_MediaAssets_ProjectId", "MediaAssets", "ProjectId");
        migrationBuilder.CreateIndex("IX_MediaAssets_CollectionKey", "MediaAssets", "CollectionKey");
        migrationBuilder.CreateIndex("IX_MediaAssets_SourceId", "MediaAssets", "SourceId");
        migrationBuilder.CreateIndex("IX_MediaProcessingJobs_MediaAssetId_JobType", "MediaProcessingJobs", new[] { "MediaAssetId", "JobType" }, unique: true);
        migrationBuilder.CreateIndex("IX_MediaProcessingJobs_Status_AvailableAfterUtc", "MediaProcessingJobs", new[] { "Status", "AvailableAfterUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MediaProcessingJobs");
        migrationBuilder.DropTable("MediaAssets");
        migrationBuilder.DropTable("MediaLibrarySources");
    }
}
