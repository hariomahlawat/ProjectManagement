using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Admin;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Tests.MediaLibrary;

// Admin review 2, PR 9 ("Figures agree"): Media sources showed "18 ready in Photos" beside
// "Prepared 17", and Media intelligence "Not sure 14" beside "Unsure guesses 13". The 18 was 17
// images and 1 video (videos get no thumbnails); the 13 was part of the 14. These tests pin the
// shared definitions the pages now use.
public sealed class MediaLibraryFiguresTests
{
    [Fact]
    public async Task ReadyInPhotos_SplitsImagesAndVideos_AndAddsUpToTheTotal()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);
        db.Assets.AddRange(
            Asset(1, source, MediaAssetKind.Photo, derivative: MediaProcessingStatus.Ready),
            Asset(2, source, MediaAssetKind.Photo, derivative: MediaProcessingStatus.Ready),
            Asset(3, source, MediaAssetKind.Photo, derivative: MediaProcessingStatus.Pending),
            Asset(4, source, MediaAssetKind.Video, derivative: MediaProcessingStatus.Unsupported));
        await db.SaveChangesAsync();

        var figures = await MediaLibraryFigures.GetReadyInPhotosAsync(db.Assets, CancellationToken.None);

        Assert.Equal(3, figures.Images);
        Assert.Equal(1, figures.Videos);
        Assert.Equal(4, figures.Total);
        // Only images are prepared; a video never counts as prepared or as waiting.
        Assert.Equal(2, figures.ImagesPrepared);
    }

    [Fact]
    public async Task ReadyInPhotos_LeavesOutDeletedUnavailableAndOutsideFolderItems()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);
        var deleted = Asset(2, source, MediaAssetKind.Photo);
        deleted.IsDeleted = true;
        var missing = Asset(3, source, MediaAssetKind.Photo);
        missing.IsAvailable = false;
        missing.AvailabilityStatus = MediaAvailabilityStatus.SourceMissing;
        var outside = Asset(4, source, MediaAssetKind.Photo);
        outside.Origin = MediaAssetOrigin.ExternalFile;
        db.Assets.AddRange(Asset(1, source, MediaAssetKind.Photo), deleted, missing, outside);
        await db.SaveChangesAsync();

        var figures = await MediaLibraryFigures.GetReadyInPhotosAsync(db.Assets, CancellationToken.None);

        Assert.Equal(1, figures.Total);
    }

    [Fact]
    public async Task ImagesPrepared_CountsImagesNotProcessingJobs()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);
        db.Assets.Add(Asset(1, source, MediaAssetKind.Photo, derivative: MediaProcessingStatus.Ready));
        db.ProcessingJobs.AddRange(
            Job(1, MediaProcessingJobType.AnalyseAsset),
            Job(1, MediaProcessingJobType.ClassifyMedia));
        await db.SaveChangesAsync();

        var figures = await MediaLibraryFigures.GetReadyInPhotosAsync(db.Assets, CancellationToken.None);

        Assert.Equal(1, figures.ImagesPrepared);
        Assert.Equal(2, await db.ProcessingJobs.CountAsync(job => job.Status == MediaProcessingJobStatus.Completed));
    }

    [Fact]
    public async Task NeedsALook_IncludesScoredTooLowAsPartOfTheTotal()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);
        db.Assets.AddRange(
            Sorted(1, source, MediaClassificationDecisionStatus.NeedsReview, MediaClassification.Unknown, MediaLibraryFigures.ScoredTooLowReasonCode),
            Sorted(2, source, MediaClassificationDecisionStatus.NeedsReview, MediaClassification.Unknown, MediaLibraryFigures.ScoredTooLowReasonCode),
            Sorted(3, source, MediaClassificationDecisionStatus.NeedsReview, MediaClassification.Unknown, "PHOTO_BASELINE_INSUFFICIENT"),
            Sorted(4, source, MediaClassificationDecisionStatus.AutomaticallyAccepted, MediaClassification.Photograph, "AUTO_ACCEPTED_HIGH_CONFIDENCE"));
        await db.SaveChangesAsync();

        var figures = await MediaLibraryFigures.GetNeedsALookAsync(db.Assets, CancellationToken.None);

        Assert.Equal(3, figures.Total);
        Assert.Equal(2, figures.ScoredTooLow);
        Assert.Equal(1, figures.OtherReasons);
    }

    private static MediaAsset Sorted(
        long id,
        MediaLibrarySource source,
        MediaClassificationDecisionStatus status,
        MediaClassification classification,
        string reason)
    {
        var asset = Asset(id, source, MediaAssetKind.Photo, derivative: MediaProcessingStatus.Ready);
        asset.AnalysisStatus = MediaProcessingStatus.Ready;
        asset.ClassificationDecisionStatus = status;
        asset.Classification = classification;
        asset.ClassificationDecisionReasonCode = reason;
        return asset;
    }

    private static MediaProcessingJob Job(long assetId, MediaProcessingJobType type) => new()
    {
        MediaAssetId = assetId,
        JobType = type,
        Status = MediaProcessingJobStatus.Completed,
        AvailableAfterUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static MediaLibrarySource CreateSource() => new()
    {
        Id = Guid.NewGuid(),
        Key = "prism-test",
        Name = "PRISM test source",
        SourceType = MediaLibrarySourceType.Prism,
        IsEnabled = true,
        IsVisibleInLibrary = true
    };

    private static MediaAsset Asset(
        long id,
        MediaLibrarySource source,
        MediaAssetKind kind,
        MediaProcessingStatus derivative = MediaProcessingStatus.Ready)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaAsset
        {
            Id = id,
            SourceId = source.Id,
            Source = source,
            Origin = kind == MediaAssetKind.Video ? MediaAssetOrigin.ProjectVideo : MediaAssetOrigin.ProjectPhoto,
            Kind = kind,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            DerivativeStatus = derivative,
            SourceEntityId = $"asset:{id}",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = "Project",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = $"Item {id}",
            OriginalFileName = kind == MediaAssetKind.Video ? $"video-{id}.mp4" : $"photo-{id}.jpg",
            ContentType = kind == MediaAssetKind.Video ? "video/mp4" : "image/jpeg",
            MediaDateUtc = now,
            IndexedAtUtc = now,
            LastSeenAtUtc = now
        };
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-figures-{Guid.NewGuid():N}")
            .Options;
        return new MediaLibraryDbContext(options);
    }
}
