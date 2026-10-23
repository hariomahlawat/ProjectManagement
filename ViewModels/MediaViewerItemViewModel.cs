using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.ViewModels;

/// <summary>
/// One photo or video for the full-screen media viewer (wwwroot/js/widgets/media-viewer.js).
/// Serialised as JSON for the page, so property names are the viewer's (camelCase).
/// </summary>
public sealed record MediaViewerItemViewModel(
    string Key,
    string Kind,
    string DisplayUrl,
    string Title,
    string? Alt,
    string? Context,
    string? Date,
    string? OriginalUrl,
    string? DownloadUrl,
    string? Poster);

/// <summary>
/// Viewer items for a project's photos and videos, shared by the Overview, the project photos
/// page and the project videos page so captions, dates and links are resolved once.
/// </summary>
public static class ProjectMediaViewerItems
{
    public const string PhotoKind = "photo";
    public const string VideoKind = "video";

    public static string PhotoKey(int photoId) => $"photo-{photoId}";

    public static string VideoKey(int videoId) => $"video-{videoId}";

    /// <summary>The project's photos in gallery order: the cover first, then by position.</summary>
    public static IReadOnlyList<ProjectPhoto> InGalleryOrder(IEnumerable<ProjectPhoto> photos, int? coverPhotoId)
        => photos
            .OrderBy(photo => photo.Id == coverPhotoId ? 0 : 1)
            .ThenBy(photo => photo.Ordinal)
            .ThenBy(photo => photo.Id)
            .ToList();

    public static MediaViewerItemViewModel ForPhoto(IUrlHelper url, int projectId, string projectName, ProjectPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(photo);

        var caption = string.IsNullOrWhiteSpace(photo.Caption) ? null : photo.Caption.Trim();
        var full = url.Page("/Projects/Photos/View", new { id = projectId, photoId = photo.Id, size = "xl", v = photo.Version }) ?? string.Empty;
        var download = url.Page("/Projects/Photos/Download", new { id = projectId, photoId = photo.Id, size = "xl", format = "jpg", v = photo.Version });

        return new MediaViewerItemViewModel(
            PhotoKey(photo.Id),
            PhotoKind,
            full,
            caption ?? "Photo",
            caption ?? $"{projectName} photo",
            projectName,
            AddedOn(photo.CreatedUtc),
            full,
            download,
            null);
    }

    public static IReadOnlyList<MediaViewerItemViewModel> ForPhotos(IUrlHelper url, int projectId, string projectName, IEnumerable<ProjectPhoto> photosInOrder)
        => photosInOrder.Select(photo => ForPhoto(url, projectId, projectName, photo)).ToList();

    public static MediaViewerItemViewModel ForVideo(IUrlHelper url, int projectId, string projectName, ProjectVideo video)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(video);

        var stream = url.Page("/Projects/Videos/Stream", new { id = projectId, videoId = video.Id, v = video.Version }) ?? string.Empty;
        var poster = string.IsNullOrWhiteSpace(video.PosterStorageKey)
            ? null
            : url.Page("/Projects/Videos/Poster", new { id = projectId, videoId = video.Id, v = video.Version });

        return new MediaViewerItemViewModel(
            VideoKey(video.Id),
            VideoKind,
            stream,
            VideoTitle(video),
            null,
            projectName,
            AddedOn(video.CreatedUtc),
            stream,
            null,
            poster);
    }

    public static IReadOnlyList<MediaViewerItemViewModel> ForVideos(IUrlHelper url, int projectId, string projectName, IEnumerable<ProjectVideo> videosInOrder)
        => videosInOrder.Select(video => ForVideo(url, projectId, projectName, video)).ToList();

    /// <summary>A video's title, or its file name when it has none.</summary>
    public static string VideoTitle(ProjectVideo video)
        => string.IsNullOrWhiteSpace(video.Title)
            ? Path.GetFileNameWithoutExtension(video.OriginalFileName)
            : video.Title!.Trim();

    private static string? AddedOn(DateTime createdUtc)
    {
        if (createdUtc == default)
        {
            return null;
        }

        var utc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        var ist = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneHelper.GetIst());
        return "Added " + ist.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// A group of viewer items rendered as <c>&lt;script type="application/json" data-media-viewer-items="group"&gt;</c>.
/// Serialised with the default (strict) encoder, so a caption cannot close the script element.
/// </summary>
public sealed record MediaViewerItemsGroup(string Group, IReadOnlyList<MediaViewerItemViewModel> Items)
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
    };

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(Items, JsonOptions);
}
