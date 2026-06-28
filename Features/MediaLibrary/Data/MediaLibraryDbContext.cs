using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Data;

/// <summary>
/// Media catalogue context. Face-intelligence tables remain dormant unless the opt-in
/// People feature is enabled and its approved models pass readiness validation.
/// </summary>
public sealed class MediaLibraryDbContext : DbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_MediaLibrary";

    public MediaLibraryDbContext(DbContextOptions<MediaLibraryDbContext> options)
        : base(options)
    {
    }

    public DbSet<MediaLibrarySource> Sources => Set<MediaLibrarySource>();
    public DbSet<MediaAsset> Assets => Set<MediaAsset>();
    public DbSet<MediaProcessingJob> ProcessingJobs => Set<MediaProcessingJob>();
    public DbSet<MediaClassificationAudit> ClassificationAudits => Set<MediaClassificationAudit>();
    public DbSet<MediaFace> Faces => Set<MediaFace>();
    public DbSet<MediaFaceEmbedding> FaceEmbeddings => Set<MediaFaceEmbedding>();
    public DbSet<MediaPerson> Persons => Set<MediaPerson>();
    public DbSet<MediaPersonFace> PersonFaces => Set<MediaPersonFace>();
    public DbSet<MediaFaceReviewDecision> FaceReviewDecisions => Set<MediaFaceReviewDecision>();
    public DbSet<MediaIdentityAudit> IdentityAudits => Set<MediaIdentityAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        MediaLibraryModelConfiguration.Configure(modelBuilder);
    }
}
