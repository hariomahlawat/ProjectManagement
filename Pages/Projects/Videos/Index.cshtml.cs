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

[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectVideoService _videoService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
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
    public bool CanManage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (_userContext.User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Include(p => p.Videos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!ProjectAccessGuard.CanViewProjectMedia(project, _userContext.User))
        {
            return Forbid();
        }

        Project = project;
        CanManage = ProjectAccessGuard.CanManageProjectMedia(project, _userContext.User, _userContext.UserId);
        Videos = project.Videos
            .OrderBy(v => v.Ordinal)
            .ThenBy(v => v.Id)
            .ToList();
        ProjectRowVersion = Convert.ToBase64String(project.RowVersion);

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        int id,
        int videoId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var authorization = await LoadManagedProjectAsync(id, rowVersion, cancellationToken);
        if (authorization.Result is not null)
        {
            return authorization.Result;
        }

        if (videoId <= 0)
        {
            TempData["Error"] = "The video could not be identified.";
            return RedirectToPage(new { id });
        }

        try
        {
            var removed = await _videoService.RemoveAsync(
                id,
                videoId,
                _userContext.UserId!,
                cancellationToken);

            TempData[removed ? "Flash" : "Error"] = removed
                ? "Video removed."
                : "The video could not be found.";
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict removing video {VideoId} from project {ProjectId}", videoId, id);
            TempData["Error"] = "The project changed while you were working. Reload the page and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing video {VideoId} from project {ProjectId}", videoId, id);
            TempData["Error"] = "The video could not be removed. Please try again.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetFeaturedAsync(
        int id,
        int videoId,
        string rowVersion,
        bool featured,
        CancellationToken cancellationToken)
    {
        var authorization = await LoadManagedProjectAsync(id, rowVersion, cancellationToken);
        if (authorization.Result is not null)
        {
            return authorization.Result;
        }

        if (videoId <= 0)
        {
            TempData["Error"] = "The video could not be identified.";
            return RedirectToPage(new { id });
        }

        try
        {
            var updated = await _videoService.SetFeaturedAsync(
                id,
                videoId,
                featured,
                _userContext.UserId!,
                cancellationToken);

            TempData[updated is null ? "Error" : "Flash"] = updated is null
                ? "The video could not be found."
                : featured ? "Featured video updated." : "Featured status removed.";
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict updating video {VideoId} for project {ProjectId}", videoId, id);
            TempData["Error"] = "The project changed while you were working. Reload the page and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating video {VideoId} for project {ProjectId}", videoId, id);
            TempData["Error"] = "The video could not be updated. Please try again.";
        }

        return RedirectToPage(new { id });
    }

    private async Task<(Project? Project, IActionResult? Result)> LoadManagedProjectAsync(
        int id,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_userContext.UserId))
        {
            return (null, Forbid());
        }

        var expectedVersion = ParseRowVersion(rowVersion);
        if (expectedVersion is null)
        {
            TempData["Error"] = "This page is out of date. Reload it and try again.";
            return (null, RedirectToPage(new { id }));
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return (null, NotFound());
        }

        if (!ProjectAccessGuard.CanManageProjectMedia(project, _userContext.User, _userContext.UserId))
        {
            return (null, Forbid());
        }

        if (!project.RowVersion.SequenceEqual(expectedVersion))
        {
            TempData["Error"] = "The project changed while you were working. Reload it and try again.";
            return (null, RedirectToPage(new { id }));
        }

        return (project, null);
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
