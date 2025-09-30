using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Photos;

[Authorize]
public class ViewModel : PageModel
{
    private static readonly byte[] PlaceholderImage = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAucB9VHq7eAAAAAASUVORK5CYII=");

    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectPhotoService _photoService;

    public ViewModel(ApplicationDbContext db, IUserContext userContext, IProjectPhotoService photoService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
    }

    public async Task<IActionResult> OnGetAsync(int id, int photoId, string? size, CancellationToken cancellationToken)
    {
        var requestedSize = NormalizeSize(size);
        if (requestedSize is null)
        {
            return NotFound();
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!CanViewProject(project, userId))
        {
            return Forbid();
        }

        var photo = await _db.ProjectPhotos
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.ProjectId == id && p.Id == photoId, cancellationToken);

        if (photo is null)
        {
            return PlaceholderResult(requestedSize);
        }

        var etag = new EntityTagHeaderValue($"\"pp-{photo.ProjectId}-{photo.Id}-v{photo.Version}-{requestedSize}\"");
        var cacheHeaders = Response.GetTypedHeaders();
        cacheHeaders.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromMinutes(10)
        };
        cacheHeaders.ETag = etag;
        cacheHeaders.LastModified = DateTime.SpecifyKind(photo.UpdatedUtc, DateTimeKind.Utc);

        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch != null && ifNoneMatch.Any(tag => tag.Equals(etag)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        try
        {
            var derivative = await _photoService.OpenDerivativeAsync(id, photoId, requestedSize, cancellationToken);
            if (derivative is null)
            {
                return PlaceholderResult(requestedSize);
            }

            var result = new FileStreamResult(derivative.Value.Stream, derivative.Value.ContentType)
            {
                EnableRangeProcessing = true
            };

            return result;
        }
        catch (Exception)
        {
            return PlaceholderResult(requestedSize);
        }
    }

    private IActionResult PlaceholderResult(string size)
    {
        var etag = new EntityTagHeaderValue($"\"pp-placeholder-{size}\"");
        var headers = Response.GetTypedHeaders();
        headers.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromMinutes(5)
        };
        headers.ETag = etag;

        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch != null && ifNoneMatch.Any(tag => tag.Equals(etag)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(PlaceholderImage, "image/png");
    }

    private string? NormalizeSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return "sm";
        }

        var normalized = size.Trim().ToLowerInvariant();
        return normalized is "sm" or "md" or "xl" ? normalized : null;
    }

    private bool CanViewProject(Project project, string userId)
    {
        var principal = _userContext.User;
        if (principal.IsInRole("Admin"))
        {
            return true;
        }

        if (principal.IsInRole("HoD") && string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (principal.IsInRole("Project Officer"))
        {
            return true;
        }

        return false;
    }
}
