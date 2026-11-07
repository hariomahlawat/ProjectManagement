using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Admin;

/// <summary>
/// What is ready in Photos from PRISM's own records, split the way the admin pages show it.
/// Videos are shown as they are: they get no thumbnails or photo checks, so only images can be
/// "prepared". <see cref="ImagesPrepared"/> counts images, not processing jobs: one image can
/// have had several jobs over time, so a job count drifts away from the number of images.
/// </summary>
public sealed record MediaReadyInPhotosFigures(int Images, int Videos, int ImagesPrepared)
{
    public int Total => Images + Videos;
}

/// <summary>
/// Images the sorting was not sure about. <see cref="ScoredTooLow"/> is part of
/// <see cref="Total"/>, not a separate group; <see cref="OtherReasons"/> is the rest (a safety
/// check sent the image to a person, or the sorting could not decide at all).
/// </summary>
public sealed record MediaNeedsALookFigures(int Total, int ScoredTooLow)
{
    public int OtherReasons => Total - ScoredTooLow;
}

/// <summary>
/// One definition for each media figure the admin pages show, so Media sources, Media
/// intelligence and Classification review count the same things (docs/ux/admin-review-2-fixes.md, PR 9).
/// </summary>
public static class MediaLibraryFigures
{
    public const string ScoredTooLowReasonCode = "BELOW_CATEGORY_THRESHOLD";

    /// <summary>PRISM's own photos and videos that Photos can show now.</summary>
    public static readonly Expression<Func<MediaAsset, bool>> ReadyInPhotos = asset =>
        asset.Origin != MediaAssetOrigin.ExternalFile
        && !asset.IsDeleted
        && asset.IsAvailable
        && asset.AvailabilityStatus == MediaAvailabilityStatus.Available;

    /// <summary>An image the sorting was not sure about and a person should look at.</summary>
    public static readonly Expression<Func<MediaAsset, bool>> NeedsALook = asset =>
        asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
        || (!asset.ClassificationIsManual && asset.Classification == MediaClassification.Unknown);

    /// <summary>Of <see cref="NeedsALook"/>: the automatic sorting scored it too low to decide.</summary>
    public static readonly Expression<Func<MediaAsset, bool>> ScoredTooLow = asset =>
        !asset.ClassificationIsManual
        && asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
        && asset.ClassificationDecisionReasonCode == ScoredTooLowReasonCode;

    public static async Task<MediaReadyInPhotosFigures> GetReadyInPhotosAsync(
        IQueryable<MediaAsset> assets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var rows = await assets
            .Where(ReadyInPhotos)
            .GroupBy(asset => asset.Kind)
            .Select(group => new
            {
                Kind = group.Key,
                Count = group.Count(),
                Prepared = group.Count(asset => asset.DerivativeStatus == MediaProcessingStatus.Ready)
            })
            .ToListAsync(cancellationToken);

        var images = rows.SingleOrDefault(row => row.Kind == MediaAssetKind.Photo);
        var videos = rows.SingleOrDefault(row => row.Kind == MediaAssetKind.Video);
        return new MediaReadyInPhotosFigures(
            images?.Count ?? 0,
            videos?.Count ?? 0,
            images?.Prepared ?? 0);
    }

    /// <param name="images">The images to count, already narrowed to the ones the page shows.</param>
    public static async Task<MediaNeedsALookFigures> GetNeedsALookAsync(
        IQueryable<MediaAsset> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);

        var needsALook = images.Where(NeedsALook);
        var total = await needsALook.CountAsync(cancellationToken);
        var scoredTooLow = await needsALook.Where(ScoredTooLow).CountAsync(cancellationToken);
        return new MediaNeedsALookFigures(total, scoredTooLow);
    }
}
