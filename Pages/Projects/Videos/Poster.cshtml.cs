using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
public class PosterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectVideoService _videoService;
    private readonly IWebHostEnvironment _environment;

    public PosterModel(ApplicationDbContext db,
                       IUserContext userContext,
                       IProjectVideoService videoService,
                       IWebHostEnvironment environment)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<IActionResult> OnGetAsync(int id, int videoId, CancellationToken cancellationToken)
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
            .SingleOrDefaultAsync(v => v.ProjectId == id && v.Id == videoId, cancellationToken);

        if (video is null)
        {
            return NotFound();
        }

        var poster = await _videoService.OpenPosterAsync(id, videoId, cancellationToken);
        if (poster is not null)
        {
            var etag = new EntityTagHeaderValue($"\"pv-poster-{video.ProjectId}-{video.Id}-v{video.Version}\"");
            var headers = Response.GetTypedHeaders();
            headers.CacheControl = new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(7)
            };
            headers.ETag = etag;
            headers.LastModified = DateTime.SpecifyKind(video.UpdatedUtc, DateTimeKind.Utc);

            var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
            if (ifNoneMatch != null && ifNoneMatch.Any(tag => tag.Equals(etag)))
            {
                poster.Value.Stream.Dispose();
                return StatusCode(StatusCodes.Status304NotModified);
            }

            return File(poster.Value.Stream, poster.Value.ContentType);
        }

        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        }

        var placeholderPath = Path.Combine(webRoot, "img", "placeholders", "project-video-placeholder.svg");
        if (!System.IO.File.Exists(placeholderPath))
        {
            return NotFound();
        }

        var stream = System.IO.File.OpenRead(placeholderPath);
        return File(stream, "image/svg+xml");
    }
}
