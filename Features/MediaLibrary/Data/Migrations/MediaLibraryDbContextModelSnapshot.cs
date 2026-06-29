using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
public sealed class MediaLibraryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.19")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaLibrarySource", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            entity.Property<string>("AllowedExtensionsJson").IsRequired().ValueGeneratedOnAdd()
                .HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property<string>("ConfigurationFingerprint").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("DisconnectedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("HealthMessage").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<string>("HealthStatus").IsRequired().ValueGeneratedOnAdd()
                .HasMaxLength(64).HasColumnType("character varying(64)").HasDefaultValue("Unknown");
            entity.Property<bool>("IncludeSubfolders").HasColumnType("boolean");
            entity.Property<long>("IndexedAssetCount").HasColumnType("bigint");
            entity.Property<bool>("IsConfigurationManaged").HasColumnType("boolean");
            entity.Property<bool>("IsDeleted").HasColumnType("boolean");
            entity.Property<bool>("IsEnabled").HasColumnType("boolean");
            entity.Property<bool>("IsReadOnly").HasColumnType("boolean");
            entity.Property<bool>("IsVisibleInLibrary").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true);
            entity.Property<string>("Key").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            entity.Property<string>("LastError").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<DateTimeOffset?>("LastHealthCheckedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("LastScanCompletedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("LastScanStartedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("LastSuccessfulScanAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("Name").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            entity.Property<string>("RootPath").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<int>("ScanIntervalMinutes").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(30);
            entity.Property<DateTimeOffset?>("ScanLockExpiresAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("ScanLockedBy").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<DateTimeOffset?>("ScanRequestedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("ScanStatus").IsRequired().ValueGeneratedOnAdd()
                .HasMaxLength(64).HasColumnType("character varying(64)").HasDefaultValue("Never");
            entity.Property<string>("SourceType").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("timestamp with time zone");

            entity.HasKey("Id");
            entity.HasIndex("Key").IsUnique();
            entity.HasIndex("ScanLockExpiresAtUtc");
            entity.HasIndex("IsEnabled", "IsDeleted", "SourceType");
            entity.HasIndex("IsVisibleInLibrary", "IsDeleted");
            entity.ToTable("MediaLibrarySources");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<string>("AnalysisSignalsJson").HasColumnType("jsonb");
            entity.Property<string>("AvailabilityStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("AnalysisStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("AnalysisVersion").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<DateTimeOffset?>("AnalysedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("FaceAnalysisStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("FaceAnalysisVersion").HasMaxLength(256).HasColumnType("character varying(256)");
            entity.Property<DateTimeOffset?>("FaceAnalysedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("FaceProcessingFailureReason").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<int>("CacheVersion").HasColumnType("integer");
            entity.Property<string>("Caption").HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.Property<string>("Classification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<bool>("ClassificationIsManual").HasColumnType("boolean");
            entity.Property<string>("ClassificationUpdatedByUserId").HasMaxLength(450).HasColumnType("character varying(450)");
            entity.Property<DateTimeOffset?>("ClassifiedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("ClassifierVersion").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<double?>("ClassificationConfidence").HasColumnType("double precision");
            entity.Property<string>("PredictedClassification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<decimal>("PredictedClassificationScore").HasPrecision(5, 4).HasColumnType("numeric(5,4)");
            entity.Property<string>("ClassificationDecisionStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("ClassificationDecisionReasonCode").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("AutomaticClassificationSignalsJson").HasColumnType("jsonb");
            entity.Property<string>("AutomaticClassificationScoresJson").HasColumnType("jsonb");
            entity.Property<string>("AutomaticClassificationMetricsJson").HasColumnType("jsonb");
            entity.Property<string>("ClassificationReviewedByUserId").HasMaxLength(450).HasColumnType("character varying(450)");
            entity.Property<DateTimeOffset?>("ClassificationReviewedAt").HasColumnType("timestamp with time zone");
            entity.Property<string>("ClassificationReviewReason").HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.Property<Guid>("ClassificationConcurrencyToken").IsConcurrencyToken().HasColumnType("uuid");
            entity.Property<string>("CollectionKey").IsRequired().HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.Property<string>("ContentHash").HasMaxLength(64).HasColumnType("character varying(64)");
            entity.Property<string>("ContentType").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("ContextKey").IsRequired().HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.Property<string>("ContextSubtitle").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            entity.Property<string>("ContextTitle").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)");
            entity.Property<string>("DerivativeStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<int?>("DurationSeconds").HasColumnType("integer");
            entity.Property<DateTimeOffset?>("FileModifiedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<long>("FileSizeBytes").HasColumnType("bigint");
            entity.Property<int?>("Height").HasColumnType("integer");
            entity.Property<DateTimeOffset>("IndexedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<bool>("IsArchived").HasColumnType("boolean");
            entity.Property<bool>("IsAvailable").HasColumnType("boolean");
            entity.Property<bool>("IsCover").HasColumnType("boolean");
            entity.Property<bool>("IsDeleted").HasColumnType("boolean");
            entity.Property<string>("Kind").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            entity.Property<DateTimeOffset?>("LastAvailabilityCheckUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset>("LastSeenAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<Guid>("LastSeenScanId").HasColumnType("uuid");
            entity.Property<DateTimeOffset>("MediaDateUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("Origin").IsRequired().HasMaxLength(48).HasColumnType("character varying(48)");
            entity.Property<string>("OriginalFileName").IsRequired().HasMaxLength(260).HasColumnType("character varying(260)");
            entity.Property<string>("ParentEntityId").HasMaxLength(256).HasColumnType("character varying(256)");
            entity.Property<string>("ProcessingFailureReason").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<int?>("ProjectId").HasColumnType("integer");
            entity.Property<string>("QuickFingerprint").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("RelativePath").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<long>("SortOrder").HasColumnType("bigint");
            entity.Property<Guid>("SourceId").HasColumnType("uuid");
            entity.Property<string>("SourceEntityId").IsRequired().HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.Property<string>("SourceLabel").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            entity.Property<string>("Title").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)");
            entity.Property<string>("UnavailableReason").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<DateTimeOffset?>("UnavailableSinceUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("VersionToken").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<int?>("Width").HasColumnType("integer");

            entity.HasKey("Id");
            entity.HasIndex("AvailabilityStatus", "IsDeleted", "MediaDateUtc");
            entity.HasIndex("FaceAnalysisStatus", "FaceAnalysisVersion")
                .HasDatabaseName("IX_MediaAssets_FaceAnalysis");
            entity.HasIndex("CollectionKey");
            entity.HasIndex("ProjectId");
            entity.HasIndex("SourceId");
            entity.HasIndex("IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc", "Id")
                .HasDatabaseName("IX_MediaAssets_LibraryTimeline");
            entity.HasIndex("IsAvailable", "IsDeleted", "MediaDateUtc");
            entity.HasIndex("Kind", "Classification");
            entity.HasIndex("ClassificationDecisionStatus");
            entity.HasIndex("PredictedClassification");
            entity.HasIndex("Classification");
            entity.HasIndex("ClassificationIsManual");
            entity.HasIndex("ClassifierVersion");
            entity.HasIndex("ClassificationReviewedAt");
            entity.HasIndex("ClassificationConcurrencyToken");
            entity.HasIndex("ClassificationIsManual", "Classification");
            entity.HasIndex("Origin", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc")
                .HasDatabaseName("IX_MediaAssets_OriginTimeline");
            entity.HasIndex("ProjectId", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc")
                .HasDatabaseName("IX_MediaAssets_ProjectTimeline");
            entity.HasIndex("SourceId", "SourceEntityId").IsUnique();
            entity.ToTable("MediaAssets");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaClassificationRun", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<long>("MediaAssetId").HasColumnType("bigint");
            entity.Property<string>("ClassifierVersion").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("PredictedClassification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<decimal>("PredictedScore").HasPrecision(5, 4).HasColumnType("numeric(5,4)");
            entity.Property<string>("EffectiveClassification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("DecisionStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("DecisionReasonCode").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("CategoryScoresJson").IsRequired().ValueGeneratedOnAdd().HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property<string>("SignalsJson").IsRequired().ValueGeneratedOnAdd().HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property<string>("MetricsJson").IsRequired().ValueGeneratedOnAdd().HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property<int>("ProcessingDurationMilliseconds").HasColumnType("integer");
            entity.Property<DateTimeOffset>("CompletedAt").HasColumnType("timestamp with time zone");
            entity.Property<bool>("Succeeded").HasColumnType("boolean");
            entity.Property<string>("FailureReason").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.HasKey("Id");
            entity.HasIndex("MediaAssetId", "CompletedAt").HasDatabaseName("IX_MediaClassificationRuns_Asset_CompletedAt");
            entity.HasIndex("ClassifierVersion"); entity.HasIndex("PredictedClassification"); entity.HasIndex("DecisionStatus"); entity.HasIndex("Succeeded");
            entity.ToTable("MediaClassificationRuns");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJob", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<int>("AttemptCount").HasColumnType("integer");
            entity.Property<DateTimeOffset>("AvailableAfterUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("FailureCode").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("FailureMessage").HasMaxLength(2048).HasColumnType("character varying(2048)");
            entity.Property<string>("JobType").IsRequired().HasMaxLength(48).HasColumnType("character varying(48)");
            entity.Property<DateTimeOffset?>("LockExpiresAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("LockedBy").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<int>("MaxAttempts").HasColumnType("integer");
            entity.Property<long>("MediaAssetId").HasColumnType("bigint");
            entity.Property<DateTimeOffset?>("StartedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("timestamp with time zone");

            entity.HasKey("Id");
            entity.HasIndex("MediaAssetId", "JobType").IsUnique();
            entity.HasIndex("Status", "AvailableAfterUtc");
            entity.ToTable("MediaProcessingJobs");
        });


        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaClassificationAudit", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<DateTimeOffset>("ChangedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("AutomaticPredictedClassification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<decimal>("AutomaticPredictedScore").HasPrecision(5, 4).HasColumnType("numeric(5,4)");
            entity.Property<string>("PreviousDecisionStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("NewDecisionStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("CorrelationId").HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("ChangedByUserId").IsRequired().HasMaxLength(450).HasColumnType("character varying(450)");
            entity.Property<bool>("IsManual").HasColumnType("boolean");
            entity.Property<long>("MediaAssetId").HasColumnType("bigint");
            entity.Property<string>("NewClassification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("PreviousClassification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<string>("Reason").HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.HasKey("Id"); entity.HasIndex("MediaAssetId", "ChangedAtUtc"); entity.ToTable("MediaClassificationAudits");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFace", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            entity.Property<double?>("BlurScore").HasColumnType("double precision"); entity.Property<double?>("BrightnessScore").HasColumnType("double precision");
            entity.Property<Guid>("ConcurrencyToken").IsConcurrencyToken().HasColumnType("uuid");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<double>("DetectionConfidence").HasColumnType("double precision");
            entity.Property<string>("DetectorModelKey").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)"); entity.Property<string>("DetectorModelVersion").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<double>("Height").HasColumnType("double precision"); entity.Property<bool>("IsSuppressed").HasColumnType("boolean"); entity.Property<string>("LandmarksJson").HasColumnType("jsonb");
            entity.Property<double>("Left").HasColumnType("double precision"); entity.Property<long>("MediaAssetId").HasColumnType("bigint"); entity.Property<double?>("PoseScore").HasColumnType("double precision");
            entity.Property<string>("QualitySignalsJson").HasColumnType("jsonb");
            entity.Property<string>("QualityStatus").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)"); entity.Property<double>("QualityScore").HasColumnType("double precision");
            entity.Property<string>("ReviewThumbnailPath").HasMaxLength(1024).HasColumnType("character varying(1024)"); entity.Property<int>("SequenceNumber").HasColumnType("integer");
            entity.Property<DateTimeOffset?>("SuppressedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<string>("SuppressedByUserId").HasMaxLength(450).HasColumnType("character varying(450)");
            entity.Property<double>("Top").HasColumnType("double precision"); entity.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<double>("Width").HasColumnType("double precision");
            entity.HasKey("Id"); entity.HasIndex("MediaAssetId", "SequenceNumber").IsUnique(); entity.HasIndex("QualityStatus", "IsSuppressed"); entity.ToTable("MediaFaces");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFaceEmbedding", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<int>("Dimension").HasColumnType("integer"); entity.Property<float[]>("Embedding").IsRequired().HasColumnType("real[]");
            entity.Property<DateTimeOffset?>("InvalidatedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<Guid>("MediaFaceId").HasColumnType("uuid"); entity.Property<string>("ModelKey").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
            entity.Property<string>("ModelVersion").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)"); entity.Property<string>("Normalization").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)"); entity.Property<double>("QualityScore").HasColumnType("double precision");
            entity.HasKey("Id"); entity.HasIndex("MediaFaceId", "ModelKey", "ModelVersion", "InvalidatedAtUtc");
            entity.HasIndex("ModelKey", "ModelVersion", "Dimension", "InvalidatedAtUtc", "QualityScore").HasDatabaseName("IX_MediaFaceEmbeddings_CandidateLookup"); entity.ToTable("MediaFaceEmbeddings");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaPerson", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid"); entity.Property<Guid>("ConcurrencyToken").IsConcurrencyToken().HasColumnType("uuid"); entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<string>("CreatedByUserId").IsRequired().HasMaxLength(450).HasColumnType("character varying(450)");
            entity.Property<string>("DisplayName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)"); entity.Property<bool>("IsHidden").HasColumnType("boolean"); entity.Property<bool>("IsMinor").HasColumnType("boolean");
            entity.Property<Guid?>("MergedIntoPersonId").HasColumnType("uuid"); entity.Property<string>("NormalizedName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)"); entity.Property<Guid?>("RepresentativeFaceId").HasColumnType("uuid"); entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)"); entity.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("timestamp with time zone");
            entity.HasKey("Id"); entity.HasIndex("MergedIntoPersonId"); entity.HasIndex("NormalizedName"); entity.HasIndex("Status", "IsHidden"); entity.ToTable("MediaPersons");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaPersonFace", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<DateTimeOffset>("AssignedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<string>("AssignedByUserId").IsRequired().HasMaxLength(450).HasColumnType("character varying(450)"); entity.Property<double?>("AssignmentConfidence").HasColumnType("double precision"); entity.Property<Guid>("ConcurrencyToken").IsConcurrencyToken().HasColumnType("uuid");
            entity.Property<string>("AssignmentType").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)"); entity.Property<Guid>("MediaFaceId").HasColumnType("uuid"); entity.Property<Guid>("MediaPersonId").HasColumnType("uuid"); entity.Property<DateTimeOffset?>("RemovedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<string>("RemovedByUserId").HasMaxLength(450).HasColumnType("character varying(450)"); entity.Property<string>("RemovalReason").HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.HasKey("Id"); entity.HasIndex("MediaFaceId").IsUnique().HasFilter("\"RemovedAtUtc\" IS NULL").HasDatabaseName("UX_MediaPersonFaces_OneActiveAssignmentPerFace"); entity.HasIndex("MediaPersonId", "MediaFaceId", "RemovedAtUtc").IsUnique(); entity.HasIndex("MediaPersonId", "RemovedAtUtc", "AssignedAtUtc").HasDatabaseName("IX_MediaPersonFaces_ActivePersonTimeline"); entity.ToTable("MediaPersonFaces");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFaceReviewDecision", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<Guid?>("CandidatePersonId").HasColumnType("uuid"); entity.Property<Guid>("ConcurrencyToken").IsConcurrencyToken().HasColumnType("uuid"); entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<DateTimeOffset?>("DecidedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<string>("DecidedByUserId").HasMaxLength(450).HasColumnType("character varying(450)");
            entity.Property<string>("Decision").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)"); entity.Property<Guid>("MediaFaceId").HasColumnType("uuid"); entity.Property<string>("ModelKey").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)"); entity.Property<string>("ModelVersion").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)"); entity.Property<string>("Notes").HasMaxLength(1024).HasColumnType("character varying(1024)"); entity.Property<double?>("Similarity").HasColumnType("double precision");
            entity.HasKey("Id"); entity.HasIndex("CandidatePersonId"); entity.HasIndex("MediaFaceId").IsUnique().HasFilter("\"Decision\" = 'Ignored' AND \"CandidatePersonId\" IS NULL").HasDatabaseName("UX_MediaFaceReviewDecisions_IgnoredFace"); entity.HasIndex("MediaFaceId", "CandidatePersonId").IsUnique().HasFilter("\"Decision\" = 'Pending' AND \"CandidatePersonId\" IS NOT NULL").HasDatabaseName("UX_MediaFaceReviewDecisions_PendingCandidate"); entity.HasIndex("Decision", "CreatedAtUtc"); entity.HasIndex("MediaFaceId", "ModelKey", "ModelVersion", "Decision").HasDatabaseName("IX_MediaFaceReviewDecisions_ModelDecision"); entity.ToTable("MediaFaceReviewDecisions");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaIdentityAudit", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<string>("Action").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)"); entity.Property<Guid?>("FaceId").HasColumnType("uuid"); entity.Property<string>("MetadataJson").HasColumnType("jsonb"); entity.Property<Guid?>("NewPersonId").HasColumnType("uuid"); entity.Property<string>("Notes").HasMaxLength(1024).HasColumnType("character varying(1024)"); entity.Property<DateTimeOffset>("PerformedAtUtc").HasColumnType("timestamp with time zone"); entity.Property<string>("PerformedByUserId").IsRequired().HasMaxLength(450).HasColumnType("character varying(450)"); entity.Property<Guid?>("PersonId").HasColumnType("uuid"); entity.Property<Guid?>("PreviousPersonId").HasColumnType("uuid");
            entity.HasKey("Id"); entity.HasIndex("FaceId", "PerformedAtUtc"); entity.HasIndex("PersonId", "PerformedAtUtc").HasDatabaseName("IX_MediaIdentityAudits_Person"); entity.ToTable("MediaIdentityAudits");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaLibrarySource", "Source")
                .WithMany("Assets")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Source");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaClassificationRun", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", "MediaAsset").WithMany("ClassificationRuns").HasForeignKey("MediaAssetId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.Navigation("MediaAsset");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJob", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", "MediaAsset")
                .WithMany("ProcessingJobs")
                .HasForeignKey("MediaAssetId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("MediaAsset");
        });


        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaClassificationAudit", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", "MediaAsset").WithMany().HasForeignKey("MediaAssetId").OnDelete(DeleteBehavior.Cascade).IsRequired(); entity.Navigation("MediaAsset");
        });
        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFace", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", "MediaAsset").WithMany("Faces").HasForeignKey("MediaAssetId").OnDelete(DeleteBehavior.Cascade).IsRequired(); entity.Navigation("MediaAsset");
        });
        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFaceEmbedding", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaFace", "MediaFace").WithMany("Embeddings").HasForeignKey("MediaFaceId").OnDelete(DeleteBehavior.Cascade).IsRequired(); entity.Navigation("MediaFace");
        });
        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaPersonFace", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaFace", "MediaFace").WithMany("PersonAssignments").HasForeignKey("MediaFaceId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaPerson", "MediaPerson").WithMany("FaceAssignments").HasForeignKey("MediaPersonId").OnDelete(DeleteBehavior.Cascade).IsRequired(); entity.Navigation("MediaFace"); entity.Navigation("MediaPerson");
        });
        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFaceReviewDecision", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaPerson", "CandidatePerson").WithMany().HasForeignKey("CandidatePersonId").OnDelete(DeleteBehavior.SetNull);
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaFace", "MediaFace").WithMany().HasForeignKey("MediaFaceId").OnDelete(DeleteBehavior.Cascade).IsRequired(); entity.Navigation("CandidatePerson"); entity.Navigation("MediaFace");
        });
        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaFace", entity => { entity.Navigation("Embeddings"); entity.Navigation("PersonAssignments"); });
        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaPerson", entity => { entity.Navigation("FaceAssignments"); });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", entity =>
        {
            entity.Navigation("ClassificationRuns");
            entity.Navigation("Faces");
            entity.Navigation("ProcessingJobs");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaLibrarySource", entity =>
        {
            entity.Navigation("Assets");
        });
#pragma warning restore 612, 618
    }
}
