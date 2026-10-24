using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

// A visit's photos for the shared full-screen viewer (Pages/Shared/_MediaViewer.cshtml), so the
// record page, Edit and the cover photo open the same viewer as Photos and the project pages.
public static class VisitMediaViewerItems
{
    public const string Group = "visit-photos";

    public static string KeyFor(Guid photoId) => "photo-" + photoId.ToString("N");

    public static IReadOnlyList<MediaViewerItemViewModel> ForPhotos(
        IUrlHelper url,
        Visit visit,
        IReadOnlyList<VisitPhoto> photosInGalleryOrder)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(visit);

        var context = $"{visit.VisitorName} · {visit.DateOfVisit.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        return photosInGalleryOrder
            .Select((photo, index) => ForPhoto(url, visit, photo, index, context))
            .ToList();
    }

    private static MediaViewerItemViewModel ForPhoto(IUrlHelper url, Visit visit, VisitPhoto photo, int index, string context)
    {
        var caption = string.IsNullOrWhiteSpace(photo.Caption) ? null : photo.Caption.Trim();
        var isCover = visit.CoverPhotoId == photo.Id;
        var version = string.IsNullOrWhiteSpace(photo.VersionStamp) ? null : photo.VersionStamp;

        var display = url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = visit.Id, photoId = photo.Id, size = "xl", v = version }) ?? string.Empty;
        var original = url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = visit.Id, photoId = photo.Id, size = "original", v = version });
        var download = url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = visit.Id, photoId = photo.Id, size = "original", download = true, v = version });

        return new MediaViewerItemViewModel(
            KeyFor(photo.Id),
            ProjectMediaViewerItems.PhotoKind,
            display,
            caption ?? (isCover ? "Cover photo" : $"Photo {index + 1}"),
            caption ?? $"Photo of the visit by {visit.VisitorName}",
            context,
            AddedOn(photo.CreatedAtUtc),
            original,
            download,
            null);
    }

    private static string? AddedOn(DateTimeOffset createdUtc)
    {
        if (createdUtc == default)
        {
            return null;
        }

        var ist = TimeZoneInfo.ConvertTime(createdUtc, TimeZoneHelper.GetIst());
        return "Added " + ist.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
