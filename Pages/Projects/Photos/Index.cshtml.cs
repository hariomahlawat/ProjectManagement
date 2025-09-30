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

namespace ProjectManagement.Pages.Projects.Photos;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectPhotoService _photoService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ApplicationDbContext db,
                      IUserContext userContext,
                      IProjectPhotoService photoService,
                      ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Project Project { get; private set; } = null!;

    public IReadOnlyList<ProjectPhoto> Photos { get; private set; } = Array.Empty<ProjectPhoto>();

    public string ProjectRowVersion { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Photos)
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
        Photos = project.Photos
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();
        ProjectRowVersion = Convert.ToBase64String(project.RowVersion);

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int photoId, string rowVersion, CancellationToken cancellationToken)
    {
        if (photoId <= 0)
        {
            TempData["Error"] = "Unable to determine the photo to remove.";
            return RedirectToPage(new { id });
        }

        byte[]? rowVersionBytes = ParseRowVersion(rowVersion);
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
            .Include(p => p.Photos)
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

        var wasCover = project.CoverPhotoId == photoId;

        try
        {
            var removed = await _photoService.RemoveAsync(project.Id, photoId, userId, cancellationToken);
            if (!removed)
            {
                TempData["Error"] = "Photo could not be removed.";
                return RedirectToPage(new { id });
            }

            if (wasCover)
            {
                await SetFallbackCoverAsync(project, photoId, cancellationToken);
            }

            TempData["Flash"] = "Photo removed.";
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while removing photo {PhotoId} from project {ProjectId}", photoId, id);
            TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing photo {PhotoId} from project {ProjectId}", photoId, id);
            TempData["Error"] = "An unexpected error occurred while removing the photo.";
        }

        return RedirectToPage(new { id });
    }

    private bool UserCanManageProject(Project project, string userId)
    {
        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        if (isAdmin)
        {
            return true;
        }

        var isHoD = principal.IsInRole("HoD");
        if (isHoD && string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
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

    private async Task SetFallbackCoverAsync(Project project, int removedPhotoId, CancellationToken cancellationToken)
    {
        var nextCover = await _db.ProjectPhotos
            .Where(p => p.ProjectId == project.Id && p.Id != removedPhotoId)
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextCover is null)
        {
            return;
        }

        nextCover.IsCover = true;
        project.CoverPhotoId = nextCover.Id;
        project.CoverPhotoVersion = nextCover.Version;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
