using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize]
public class ViewPhotoModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IVisitPhotoService _photoService;

    public ViewPhotoModel(ApplicationDbContext db, IVisitPhotoService photoService)
    {
        _db = db;
        _photoService = photoService;
    }

    // download=true serves the file as an attachment with a readable name (the viewer's Download button).
    public async Task<IActionResult> OnGetAsync(Guid id, Guid photoId, string? size, bool download, CancellationToken cancellationToken)
    {
        var normalizedSize = NormalizeSize(size);
        if (normalizedSize is null)
        {
            return NotFound();
        }

        var visitDate = await _db.Visits.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => (DateOnly?)v.DateOfVisit)
            .FirstOrDefaultAsync(cancellationToken);
        if (visitDate is null)
        {
            return NotFound();
        }

        var asset = await _photoService.OpenAsync(id, photoId, normalizedSize, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        var headers = Response.GetTypedHeaders();
        headers.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(7)
        };
        headers.LastModified = asset.LastModifiedUtc.UtcDateTime;

        var etag = new EntityTagHeaderValue($"\"visit-photo-{id}-{photoId}-{normalizedSize}{(download ? "-dl" : string.Empty)}\"");
        headers.ETag = etag;

        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch != null && ifNoneMatch.Any(tag => tag.Equals(etag)))
        {
            asset.Stream.Dispose();
            return StatusCode(304);
        }

        var result = new FileStreamResult(asset.Stream, asset.ContentType)
        {
            EnableRangeProcessing = true
        };

        if (download)
        {
            result.FileDownloadName = DownloadName(visitDate.Value, photoId, asset.ContentType);
        }

        return result;
    }

    // e.g. visit-2026-06-15-7e557574.jpg: sorts by date and stays unique per photo.
    internal static string DownloadName(DateOnly dateOfVisit, Guid photoId, string? contentType)
    {
        var extension = contentType?.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg"
        };

        return $"visit-{dateOfVisit:yyyy-MM-dd}-{photoId.ToString("N")[..8]}.{extension}";
    }

    private static string? NormalizeSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return "sm";
        }

        var normalized = size.Trim().ToLowerInvariant();
        return normalized switch
        {
            "xs" or "sm" or "md" or "xl" or "original" => normalized,
            _ => null
        };
    }
}
