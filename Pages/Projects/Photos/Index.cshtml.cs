using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    [BindProperty]
    public GalleryOrderInput Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public IReadOnlyList<ProjectPhoto> Photos { get; private set; } = Array.Empty<ProjectPhoto>();

    public string ProjectRowVersion { get; private set; } = string.Empty;

    public bool CanReorder => Photos.Count > 1;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var result = await LoadProjectAsync(id, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        PopulateOrderInput();
        return Page();
    }

    public async Task<IActionResult> OnPostReorderAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        Input.Items ??= new List<GalleryOrderItem>();
        var submittedRowVersion = ParseRowVersion(Input.RowVersion);
        var submittedGalleryVersion = Input.GalleryVersion;

        var result = await LoadProjectAsync(id, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        if (submittedRowVersion is null)
        {
            ResetOrderInputForCurrentGallery("The form has expired. The current photo order has been reloaded; review it and try again.");
            return Page();
        }

        if (!Project.RowVersion.SequenceEqual(submittedRowVersion))
        {
            ResetOrderInputForCurrentGallery("The project was updated by someone else. The current photo order has been reloaded; review it and try again.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(submittedGalleryVersion) ||
            !string.Equals(submittedGalleryVersion, ComputeGalleryVersion(Photos), StringComparison.Ordinal))
        {
            ResetOrderInputForCurrentGallery("The gallery changed while you were working. The current photos and order have been reloaded; review them and try again.");
            return Page();
        }

        var expectedIds = Photos.Select(photo => photo.Id).OrderBy(photoId => photoId).ToArray();
        var suppliedIds = Input.Items.Select(item => item.PhotoId).OrderBy(photoId => photoId).ToArray();
        if (!expectedIds.SequenceEqual(suppliedIds))
        {
            ResetOrderInputForCurrentGallery("The gallery changed while you were reordering it. The current photos have been reloaded; review the order and try again.");
            return Page();
        }

        if (Input.Items.Any(item => item.Ordinal <= 0))
        {
            ModelState.AddModelError(string.Empty, "Each photo must have a valid positive position.");
        }

        if (Input.Items.Select(item => item.Ordinal).Distinct().Count() != Input.Items.Count)
        {
            ModelState.AddModelError(string.Empty, "Each photo must have a unique position.");
        }

        Input.RowVersion = ProjectRowVersion;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var orderedPhotoIds = Input.Items
            .OrderBy(item => item.Ordinal)
            .ThenBy(item => item.PhotoId)
            .Select(item => item.PhotoId)
            .ToList();

        try
        {
            await _photoService.ReorderAsync(Project.Id, orderedPhotoIds, _userContext.UserId!, cancellationToken);
            TempData["Flash"] = "Photo order updated.";
            return RedirectToPage(new { id });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while reordering photos for project {ProjectId}", id);
            _db.ChangeTracker.Clear();
            var reloadResult = await LoadProjectAsync(id, cancellationToken);
            if (reloadResult is not null)
            {
                return reloadResult;
            }

            ResetOrderInputForCurrentGallery("The gallery was updated by someone else. The current photo order has been reloaded; review it and try again.");
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error reordering photos for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty, "The photo order could not be saved. Please try again.");
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int photoId, int photoVersion, string rowVersion, CancellationToken cancellationToken)
    {
        if (photoId <= 0)
        {
            TempData["Error"] = "Unable to determine the photo to remove.";
            return RedirectToPage(new { id });
        }

        var rowVersionBytes = ParseRowVersion(rowVersion);
        if (rowVersionBytes is null)
        {
            TempData["Error"] = "The form has expired. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var result = await LoadProjectAsync(id, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        if (!Project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var currentPhoto = Photos.FirstOrDefault(photo => photo.Id == photoId);
        if (currentPhoto is null)
        {
            TempData["Error"] = "The photo is no longer available.";
            return RedirectToPage(new { id });
        }

        if (photoVersion <= 0 || currentPhoto.Version != photoVersion)
        {
            TempData["Error"] = "The photo was updated by someone else. Review it before removing it.";
            return RedirectToPage(new { id });
        }

        try
        {
            var removed = await _photoService.RemoveAsync(Project.Id, photoId, _userContext.UserId!, cancellationToken);
            if (!removed)
            {
                TempData["Error"] = "Photo could not be removed.";
                return RedirectToPage(new { id });
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

    private async Task<IActionResult?> LoadProjectAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(project => project.Photos)
            .SingleOrDefaultAsync(project => project.Id == id, cancellationToken);

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
            .OrderBy(photo => photo.Ordinal)
            .ThenBy(photo => photo.Id)
            .ToList();
        ProjectRowVersion = Convert.ToBase64String(project.RowVersion);
        return null;
    }

    private void PopulateOrderInput()
    {
        Input = new GalleryOrderInput
        {
            ProjectId = Project.Id,
            RowVersion = ProjectRowVersion,
            GalleryVersion = ComputeGalleryVersion(Photos),
            Items = Photos
                .Select(photo => new GalleryOrderItem
                {
                    PhotoId = photo.Id,
                    Ordinal = photo.Ordinal
                })
                .ToList()
        };
    }

    private static string ComputeGalleryVersion(IEnumerable<ProjectPhoto> photos)
    {
        var signature = string.Join("|", photos
            .OrderBy(photo => photo.Ordinal)
            .ThenBy(photo => photo.Id)
            .Select(photo => $"{photo.Id}:{photo.Version}:{photo.Ordinal}"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return Convert.ToHexString(digest);
    }

    private void ResetOrderInputForCurrentGallery(string message)
    {
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, message);
        PopulateOrderInput();
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

    public sealed class GalleryOrderInput
    {
        public int ProjectId { get; set; }

        public string RowVersion { get; set; } = string.Empty;

        public string GalleryVersion { get; set; } = string.Empty;

        public List<GalleryOrderItem> Items { get; set; } = new();
    }

    public sealed class GalleryOrderItem
    {
        public int PhotoId { get; set; }

        public int Ordinal { get; set; }
    }
}
