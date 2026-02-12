using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Photos;

[Authorize]
public class DownloadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectPhotoService _photoService;

    public DownloadModel(ApplicationDbContext db, IUserContext userContext, IProjectPhotoService photoService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
    }

    public async Task<IActionResult> OnGetAsync(int id, int photoId, string? size, string? format, CancellationToken cancellationToken)
    {
        // SECTION: Validate request options
        var requestedSize = NormalizeSize(size);
        var requestedFormat = NormalizeFormat(format);
        if (requestedSize is null || requestedFormat is null)
        {
            return NotFound();
        }

        // SECTION: Validate authenticated user
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        // SECTION: Authorize project access
        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!ProjectAccessGuard.CanViewProject(project, _userContext.User, userId))
        {
            return Forbid();
        }

        // SECTION: Verify photo belongs to project
        var photoExists = await _db.ProjectPhotos
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == id && p.Id == photoId, cancellationToken);

        if (!photoExists)
        {
            return NotFound();
        }

        // SECTION: Resolve requested derivative stream by explicit format
        var derivative = await _photoService.OpenDerivativeAsync(id, photoId, requestedSize, requestedFormat, cancellationToken);
        if (derivative is null)
        {
            return NotFound();
        }

        var extension = requestedFormat is "jpeg" ? "jpg" : requestedFormat;
        var fileName = $"project-{id}-photo-{photoId}-{requestedSize}.{extension}";

        // SECTION: Return downloadable file response
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();

        var result = new FileStreamResult(derivative.Value.Stream, derivative.Value.ContentType)
        {
            FileDownloadName = fileName,
            EnableRangeProcessing = true
        };

        return result;
    }

    private static string? NormalizeSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return "sm";
        }

        var normalized = size.Trim().ToLowerInvariant();
        return normalized is "xs" or "sm" or "md" or "xl" ? normalized : null;
    }

    private static string? NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        var normalized = format.Trim().ToLowerInvariant();
        return normalized is "webp" or "png" or "jpg" or "jpeg" ? normalized : null;
    }
}
