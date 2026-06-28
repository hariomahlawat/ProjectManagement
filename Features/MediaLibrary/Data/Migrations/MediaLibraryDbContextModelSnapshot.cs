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
            entity.Property<int>("CacheVersion").HasColumnType("integer");
            entity.Property<string>("Caption").HasMaxLength(1024).HasColumnType("character varying(1024)");
            entity.Property<string>("Classification").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            entity.Property<double?>("ClassificationConfidence").HasColumnType("double precision");
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
            entity.HasIndex("CollectionKey");
            entity.HasIndex("ProjectId");
            entity.HasIndex("SourceId");
            entity.HasIndex("IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc", "Id")
                .HasDatabaseName("IX_MediaAssets_LibraryTimeline");
            entity.HasIndex("IsAvailable", "IsDeleted", "MediaDateUtc");
            entity.HasIndex("Kind", "Classification");
            entity.HasIndex("Origin", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc")
                .HasDatabaseName("IX_MediaAssets_OriginTimeline");
            entity.HasIndex("ProjectId", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc")
                .HasDatabaseName("IX_MediaAssets_ProjectTimeline");
            entity.HasIndex("SourceId", "SourceEntityId").IsUnique();
            entity.ToTable("MediaAssets");
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

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", entity =>
        {
            entity.HasOne("ProjectManagement.Features.MediaLibrary.Domain.MediaLibrarySource", "Source")
                .WithMany("Assets")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("Source");
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

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaAsset", entity =>
        {
            entity.Navigation("ProcessingJobs");
        });

        modelBuilder.Entity("ProjectManagement.Features.MediaLibrary.Domain.MediaLibrarySource", entity =>
        {
            entity.Navigation("Assets");
        });
#pragma warning restore 612, 618
    }
}
