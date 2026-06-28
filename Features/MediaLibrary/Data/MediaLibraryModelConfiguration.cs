using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Data;

public static class MediaLibraryModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Ignore<MediaFaceCluster>();

        modelBuilder.Entity<MediaLibrarySource>(entity =>
        {
            entity.ToTable("MediaLibrarySources");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.RootPath).HasMaxLength(2048);
            entity.Property(x => x.AllowedExtensionsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(x => x.ConfigurationFingerprint).HasMaxLength(128);
            entity.Property(x => x.ScanStatus).HasMaxLength(64).HasDefaultValue("Never");
            entity.Property(x => x.ScanLockedBy).HasMaxLength(128);
            entity.Property(x => x.LastError).HasMaxLength(2048);
            entity.Property(x => x.HealthStatus).HasMaxLength(64).HasDefaultValue("Unknown");
            entity.Property(x => x.HealthMessage).HasMaxLength(2048);
            entity.Property(x => x.ScanIntervalMinutes).HasDefaultValue(30);
            entity.Property(x => x.IsVisibleInLibrary).HasDefaultValue(true);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => new { x.IsEnabled, x.IsDeleted, x.SourceType });
            entity.HasIndex(x => new { x.IsVisibleInLibrary, x.IsDeleted });
            entity.HasIndex(x => x.ScanLockExpiresAtUtc);
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable("MediaAssets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Origin).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.SourceEntityId).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ParentEntityId).HasMaxLength(256);
            entity.Property(x => x.RelativePath).HasMaxLength(2048);
            entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.QuickFingerprint).HasMaxLength(128);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.ContextKey).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.CollectionKey).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ContextTitle).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContextSubtitle).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SourceLabel).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Caption).HasMaxLength(1024);
            entity.Property(x => x.VersionToken).HasMaxLength(128);
            entity.Property(x => x.Classification).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.DerivativeStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.AnalysisStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.AnalysisVersion).HasMaxLength(128);
            entity.Property(x => x.ClassificationUpdatedByUserId).HasMaxLength(450);
            entity.Property(x => x.ClassifierVersion).HasMaxLength(128);
            entity.Property(x => x.AnalysisSignalsJson).HasColumnType("jsonb");
            entity.Property(x => x.ProcessingFailureReason).HasMaxLength(2048);
            entity.Property(x => x.AvailabilityStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.UnavailableReason).HasMaxLength(2048);
            entity.HasIndex(x => new { x.SourceId, x.SourceEntityId }).IsUnique();
            entity.HasIndex(x => new { x.IsAvailable, x.IsDeleted, x.MediaDateUtc });
            entity.HasIndex(x => new { x.AvailabilityStatus, x.IsDeleted, x.MediaDateUtc });
            entity.HasIndex(x => new { x.IsAvailable, x.IsDeleted, x.IsArchived, x.MediaDateUtc, x.Id })
                .HasDatabaseName("IX_MediaAssets_LibraryTimeline");
            entity.HasIndex(x => new { x.Origin, x.IsAvailable, x.IsDeleted, x.IsArchived, x.MediaDateUtc })
                .HasDatabaseName("IX_MediaAssets_OriginTimeline");
            entity.HasIndex(x => new { x.ProjectId, x.IsAvailable, x.IsDeleted, x.IsArchived, x.MediaDateUtc })
                .HasDatabaseName("IX_MediaAssets_ProjectTimeline");
            entity.HasIndex(x => new { x.Kind, x.Classification });
            entity.HasIndex(x => new { x.ClassificationIsManual, x.Classification });
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.CollectionKey);
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Assets)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaClassificationAudit>(entity =>
        {
            entity.ToTable("MediaClassificationAudits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PreviousClassification).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.NewClassification).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.ChangedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1024);
            entity.HasIndex(x => new { x.MediaAssetId, x.ChangedAtUtc });
            entity.HasOne(x => x.MediaAsset).WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaProcessingJob>(entity =>
        {
            entity.ToTable("MediaProcessingJobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.JobType).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.LockedBy).HasMaxLength(128);
            entity.Property(x => x.FailureCode).HasMaxLength(128);
            entity.Property(x => x.FailureMessage).HasMaxLength(2048);
            entity.HasIndex(x => new { x.MediaAssetId, x.JobType }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.AvailableAfterUtc });
            entity.HasOne(x => x.MediaAsset)
                .WithMany(x => x.ProcessingJobs)
                .HasForeignKey(x => x.MediaAssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<MediaFace>(entity =>
        {
            entity.ToTable("MediaFaces"); entity.HasKey(x => x.Id);
            entity.Property(x => x.LandmarksJson).HasColumnType("jsonb");
            entity.Property(x => x.QualityStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.DetectorModelKey).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DetectorModelVersion).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReviewThumbnailPath).HasMaxLength(1024);
            entity.Property(x => x.SuppressedByUserId).HasMaxLength(450);
            entity.HasIndex(x => new { x.MediaAssetId, x.SequenceNumber }).IsUnique();
            entity.HasIndex(x => new { x.QualityStatus, x.IsSuppressed });
            entity.HasOne(x => x.MediaAsset).WithMany(x => x.Faces).HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MediaFaceEmbedding>(entity =>
        {
            entity.ToTable("MediaFaceEmbeddings"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Embedding).HasColumnType("real[]").IsRequired();
            entity.Property(x => x.ModelKey).HasMaxLength(128).IsRequired(); entity.Property(x => x.ModelVersion).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Normalization).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.MediaFaceId, x.ModelKey, x.ModelVersion, x.InvalidatedAtUtc });
            entity.HasOne(x => x.MediaFace).WithMany(x => x.Embeddings).HasForeignKey(x => x.MediaFaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MediaPerson>(entity =>
        {
            entity.ToTable("MediaPersons"); entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(x => x.NormalizedName); entity.HasIndex(x => new { x.Status, x.IsHidden });
        });
        modelBuilder.Entity<MediaPersonFace>(entity =>
        {
            entity.ToTable("MediaPersonFaces"); entity.HasKey(x => x.Id);
            entity.Property(x => x.AssignmentType).HasConversion<string>().HasMaxLength(32).IsRequired(); entity.Property(x => x.AssignedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(x => new { x.MediaPersonId, x.MediaFaceId, x.RemovedAtUtc }).IsUnique();
            entity.HasOne(x => x.MediaPerson).WithMany(x => x.FaceAssignments).HasForeignKey(x => x.MediaPersonId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.MediaFace).WithMany(x => x.PersonAssignments).HasForeignKey(x => x.MediaFaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MediaFaceReviewDecision>(entity =>
        {
            entity.ToTable("MediaFaceReviewDecisions"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32).IsRequired(); entity.Property(x => x.DecidedByUserId).HasMaxLength(450); entity.Property(x => x.Notes).HasMaxLength(1024);
            entity.HasIndex(x => new { x.Decision, x.CreatedAtUtc });
            entity.HasOne(x => x.MediaFace).WithMany().HasForeignKey(x => x.MediaFaceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CandidatePerson).WithMany().HasForeignKey(x => x.CandidatePersonId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<MediaIdentityAudit>(entity =>
        {
            entity.ToTable("MediaIdentityAudits"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired(); entity.Property(x => x.PerformedByUserId).HasMaxLength(450).IsRequired(); entity.Property(x => x.Notes).HasMaxLength(1024);
            entity.HasIndex(x => new { x.FaceId, x.PerformedAtUtc });
        });
    }
}
