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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        MediaLibraryModelConfiguration.Configure(modelBuilder);
    }
}
