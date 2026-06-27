using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Data;

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
    public DbSet<MediaPerson> People => Set<MediaPerson>();
    public DbSet<MediaFaceCluster> FaceClusters => Set<MediaFaceCluster>();
    public DbSet<MediaFace> Faces => Set<MediaFace>();
    public DbSet<MediaIdentityAudit> IdentityAudits => Set<MediaIdentityAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        MediaLibraryModelConfiguration.Configure(modelBuilder);
    }
}
