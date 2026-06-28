using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Data;

/// <summary>
/// Core catalogue context. People/face tables are deliberately excluded and will use a
/// separate opt-in migration when that feature is approved.
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
