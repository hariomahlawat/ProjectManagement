using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Where one PRISM Photos item is shown, opened and downloaded from. Project, visit and
/// social-media photos are served by their own pages, which make their own access checks;
/// activity photos and photos from other folders go through <c>/Photos/Media</c>.
/// </summary>
public sealed record MediaLibraryItemAddresses(
    string Thumbnail,
    string? GridThumbnail,
    string Display,
    string Original,
    string? Download,
    string? Source);

/// <summary>
/// The one place that decides each kind of library item's addresses, used by the PRISM Photos
/// page and by the Collage Studio picker, so both ask the same page (and its access check) for
/// a picture. The caller supplies how a page address is made (<c>Url.Page</c> on a Razor page,
/// <c>LinkGenerator</c> in an API) and how an app file address is made.
/// </summary>
public static class MediaLibraryItemAddressResolver
{
    /// <summary>The size asked for the small pictures of the photo grid.</summary>
    public const string GridThumbnailSize = "sm";

    public const string VideoPlaceholder = "~/img/placeholders/project-video-placeholder.svg";

    public static MediaLibraryItemAddresses Resolve(
        MediaLibraryQueryItem row,
        Func<string, object, string?> page,
        Func<string, string> content)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(content);

        var id = EntityIdOf(row.SourceEntityId);
        var parentId = row.ParentEntityId ?? ParentIdOf(row.ContextKey);
        var version = row.VersionToken;

        string thumbnail;
        string display;
        string original;
        string? gridThumbnail = null;
        string? download = null;
        string? source;

        switch (row.Origin)
        {
            case MediaAssetOrigin.ProjectPhoto:
                display = page("/Projects/Photos/View", new { id = parentId, photoId = id, size = "xl", v = version }) ?? string.Empty;
                thumbnail = page("/Projects/Photos/View", new { id = parentId, photoId = id, size = "md", v = version }) ?? display;
                gridThumbnail = page("/Projects/Photos/View", new { id = parentId, photoId = id, size = GridThumbnailSize, v = version });
                original = page("/Projects/Photos/View", new { id = parentId, photoId = id, size = "original", v = version }) ?? display;
                download = page("/Projects/Photos/Download", new { id = parentId, photoId = id, size = "original" });
                source = page("/Projects/Photos/Index", new { id = parentId });
                break;

            case MediaAssetOrigin.ProjectVideo:
                thumbnail = page("/Projects/Videos/Poster", new { id = parentId, videoId = id, v = version })
                            ?? content(VideoPlaceholder);
                display = page("/Projects/Videos/Stream", new { id = parentId, videoId = id, v = version }) ?? string.Empty;
                original = display;
                source = page("/Projects/Videos/Index", new { id = parentId });
                break;

            case MediaAssetOrigin.VisitPhoto:
                display = page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "xl", v = version }) ?? string.Empty;
                thumbnail = page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "md", v = version }) ?? display;
                gridThumbnail = page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = GridThumbnailSize, v = version });
                original = page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "original", v = version }) ?? display;
                source = page("/Visits/Details", new { area = "ProjectOfficeReports", id = parentId });
                break;

            case MediaAssetOrigin.SocialMediaEventPhoto:
                display = page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "story", v = version }) ?? string.Empty;
                thumbnail = page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "feed", v = version }) ?? display;
                original = page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "original", v = version }) ?? display;
                source = page("/SocialMedia/Details", new { area = "ProjectOfficeReports", id = parentId });
                break;

            case MediaAssetOrigin.ActivityPhoto:
                display = page("/Photos/Media", new { id = row.Id, variant = "preview", v = row.CacheVersion }) ?? string.Empty;
                thumbnail = page("/Photos/Media", new { id = row.Id, variant = "thumb", v = row.CacheVersion }) ?? display;
                original = page("/Photos/Media", new { id = row.Id, variant = "original", v = row.CacheVersion }) ?? display;
                download = page("/Photos/Media", new { id = row.Id, variant = "original", download = true, v = row.CacheVersion });
                source = page("/Activities/Details", new { id = parentId });
                break;

            default:
                display = page("/Photos/Media", new
                {
                    id = row.Id,
                    variant = row.Kind == MediaAssetKind.Photo ? "preview" : "original",
                    v = row.CacheVersion
                }) ?? string.Empty;
                thumbnail = row.Kind == MediaAssetKind.Photo
                    ? page("/Photos/Media", new { id = row.Id, variant = "thumb", v = row.CacheVersion }) ?? display
                    : content(VideoPlaceholder);
                original = page("/Photos/Media", new { id = row.Id, variant = "original", v = row.CacheVersion }) ?? display;
                download = page("/Photos/Media", new { id = row.Id, variant = "original", download = true, v = row.CacheVersion });
                source = page("/Photos/Index", new { Source = "external", Q = row.ParentEntityId });
                break;
        }

        return new MediaLibraryItemAddresses(thumbnail, gridThumbnail, display, original, download, source);
    }

    /// <summary>The source record's own id: the part after "kind:" in the catalogue's source id.</summary>
    public static string EntityIdOf(string sourceEntityId)
    {
        var separator = sourceEntityId.IndexOf(':');
        return separator >= 0 && separator + 1 < sourceEntityId.Length
            ? sourceEntityId[(separator + 1)..]
            : sourceEntityId;
    }

    /// <summary>The parent record's id: the part after "kind:" in the catalogue's context key.</summary>
    public static string ParentIdOf(string contextKey)
    {
        var separator = contextKey.IndexOf(':');
        return separator >= 0 && separator + 1 < contextKey.Length
            ? contextKey[(separator + 1)..]
            : contextKey;
    }
}
