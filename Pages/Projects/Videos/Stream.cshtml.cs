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

namespace ProjectManagement.Pages.Projects.Videos;

[Authorize]
public class StreamModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectVideoService _videoService;

    public StreamModel(ApplicationDbContext db, IUserContext userContext, IProjectVideoService videoService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
    }

    public async Task<IActionResult> OnGetAsync(int id, int videoId, int? v, CancellationToken cancellationToken)
    {
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

        if (!ProjectAccessGuard.CanViewProject(project, _userContext.User, userId))
        {
            return Forbid();
        }

        var video = await _db.ProjectVideos
            .AsNoTracking()
            .SingleOrDefaultAsync(vd => vd.ProjectId == id && vd.Id == videoId, cancellationToken);

        if (video is null)
        {
            return NotFound();
        }

        var etagValue = new EntityTagHeaderValue($"\"pv-{video.ProjectId}-{video.Id}-v{video.Version}\"");
        var headers = Response.GetTypedHeaders();
        headers.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(7)
        };
        headers.ETag = etagValue;
        headers.LastModified = DateTime.SpecifyKind(video.UpdatedUtc, DateTimeKind.Utc);

        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch != null && ifNoneMatch.Any(tag => tag.Equals(etagValue)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var streamResult = await _videoService.OpenOriginalAsync(id, videoId, cancellationToken);
        if (streamResult is null)
        {
            return NotFound();
        }

        var result = new FileStreamResult(streamResult.Value.Stream, streamResult.Value.ContentType)
        {
            EnableRangeProcessing = true
        };

        if (v.HasValue && v.Value != video.Version)
        {
            // Force cache busting when version mismatch occurs.
            Response.Headers[HeaderNames.CacheControl] = "no-cache";
        }

        return result;
    }
}
