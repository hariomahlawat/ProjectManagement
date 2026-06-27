using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Data;

public static class MediaLibraryModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");

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
            entity.Property(x => x.LastError).HasMaxLength(2048);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => new { x.IsEnabled, x.SourceType });
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
            entity.Property(x => x.AnalysisSignalsJson).HasColumnType("jsonb");
            entity.Property(x => x.ProcessingFailureReason).HasMaxLength(2048);
            entity.HasIndex(x => new { x.SourceId, x.SourceEntityId }).IsUnique();
            entity.HasIndex(x => new { x.IsAvailable, x.IsDeleted, x.MediaDateUtc });
            entity.HasIndex(x => new { x.Kind, x.Classification });
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.CollectionKey);
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Assets)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<MediaPerson>(entity =>
        {
            entity.ToTable("MediaPeople");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LinkedUserId).HasMaxLength(450);
            entity.Property(x => x.Designation).HasMaxLength(200);
            entity.Property(x => x.Organisation).HasMaxLength(200);
            entity.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(x => x.NormalizedName);
            entity.HasIndex(x => x.LinkedUserId);
        });

        modelBuilder.Entity<MediaFaceCluster>(entity =>
        {
            entity.ToTable("MediaFaceClusters");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.PersonId);
            entity.HasOne(x => x.Person)
                .WithMany(x => x.Clusters)
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaFace>(entity =>
        {
            entity.ToTable("MediaFaces");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Embedding).HasColumnType("vector(512)");
            entity.Property(x => x.IdentityStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.DetectorModelVersion).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EmbeddingModelVersion).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.MediaAssetId);
            entity.HasIndex(x => x.PersonId);
            entity.HasIndex(x => x.FaceClusterId);
            entity.HasIndex(x => x.Embedding)
                .HasDatabaseName("IX_MediaFaces_Embedding_Hnsw")
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops")
                .HasFilter("\"Embedding\" IS NOT NULL");
            entity.HasOne(x => x.MediaAsset)
                .WithMany(x => x.Faces)
                .HasForeignKey(x => x.MediaAssetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Person)
                .WithMany(x => x.Faces)
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.FaceCluster)
                .WithMany(x => x.Faces)
                .HasForeignKey(x => x.FaceClusterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaIdentityAudit>(entity =>
        {
            entity.ToTable("MediaIdentityAudits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PerformedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1024);
            entity.HasIndex(x => new { x.FaceId, x.PerformedAtUtc });
        });
    }
}
