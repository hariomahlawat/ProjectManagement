using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Videos;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectVideoService _videoService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ApplicationDbContext db,
                      IUserContext userContext,
                      IProjectVideoService videoService,
                      ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Project Project { get; private set; } = null!;

    public IReadOnlyList<ProjectVideo> Videos { get; private set; } = Array.Empty<ProjectVideo>();

    public string ProjectRowVersion { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Videos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageProject(project, userId))
        {
            return Forbid();
        }

        Project = project;
        Videos = project.Videos
            .OrderBy(v => v.Ordinal)
            .ThenBy(v => v.Id)
            .ToList();
        ProjectRowVersion = Convert.ToBase64String(project.RowVersion);

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int videoId, string rowVersion, CancellationToken cancellationToken)
    {
        if (videoId <= 0)
        {
            TempData["Error"] = "Unable to determine the video to remove.";
            return RedirectToPage(new { id });
        }

        var rowVersionBytes = ParseRowVersion(rowVersion);
        if (rowVersionBytes is null)
        {
            TempData["Error"] = "The form has expired. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Videos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageProject(project, userId))
        {
            return Forbid();
        }

        if (!project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        try
        {
            var removed = await _videoService.RemoveAsync(project.Id, videoId, userId, cancellationToken);
            if (!removed)
            {
                TempData["Error"] = "Video could not be removed.";
                return RedirectToPage(new { id });
            }

            TempData["Flash"] = "Video removed.";
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while removing video {VideoId} from project {ProjectId}", videoId, id);
            TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing video {VideoId} from project {ProjectId}", videoId, id);
            TempData["Error"] = "An unexpected error occurred while removing the video.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetFeaturedAsync(int id,
                                                            int videoId,
                                                            string rowVersion,
                                                            bool featured,
                                                            CancellationToken cancellationToken)
    {
        if (videoId <= 0)
        {
            TempData["Error"] = "Unable to determine the video.";
            return RedirectToPage(new { id });
        }

        var rowVersionBytes = ParseRowVersion(rowVersion);
        if (rowVersionBytes is null)
        {
            TempData["Error"] = "The form has expired. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Videos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageProject(project, userId))
        {
            return Forbid();
        }

        if (!project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        try
        {
            var updated = await _videoService.SetFeaturedAsync(project.Id, videoId, featured, userId, cancellationToken);
            if (updated is null)
            {
                TempData["Error"] = "Video could not be updated.";
                return RedirectToPage(new { id });
            }

            TempData["Flash"] = featured ? "Video set as featured." : "Featured video cleared.";
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while updating featured video {VideoId} for project {ProjectId}", videoId, id);
            TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating featured video {VideoId} for project {ProjectId}", videoId, id);
            TempData["Error"] = "An unexpected error occurred while updating the video.";
        }

        return RedirectToPage(new { id });
    }

    private bool UserCanManageProject(Project project, string userId)
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

        return string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? ParseRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
