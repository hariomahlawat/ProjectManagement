using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectPhotoService _photoService;
    private readonly ILogger<EditModel> _logger;

    public EditModel(ApplicationDbContext db,
                     IUserContext userContext,
                     IProjectPhotoService photoService,
                     ILogger<EditModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public EditInput Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public ProjectPhoto Photo { get; private set; } = null!;

    public ProjectPhoto? CurrentCoverPhoto { get; private set; }

    public bool IsCurrentCover => CurrentCoverPhoto is not null && CurrentCoverPhoto.Id == Photo.Id;

    public bool WillReplaceAnotherCover => CurrentCoverPhoto is not null && CurrentCoverPhoto.Id != Photo.Id;

    public bool AllowTotLinking => Project?.Tot is { Status: not ProjectTotStatus.NotRequired };

    public string TotStatusDisplay => Project?.Tot?.Status switch
    {
        ProjectTotStatus.NotRequired => "Not required",
        ProjectTotStatus.NotStarted => "Not started",
        ProjectTotStatus.InProgress => "In progress",
        ProjectTotStatus.Completed => "Completed",
        _ => "Unknown"
    };

    public async Task<IActionResult> OnGetAsync(int id, int photoId, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .Include(p => p.Photos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!ProjectAccessGuard.CanManageProjectMedia(project, _userContext.User, userId))
        {
            return Forbid();
        }

        var photo = project.Photos.SingleOrDefault(p => p.Id == photoId);
        if (photo is null)
        {
            return NotFound();
        }

        Project = project;
        Photo = photo;
        CurrentCoverPhoto = ResolveCoverPhoto(project);
        Input = new EditInput
        {
            ProjectId = project.Id,
            PhotoId = photo.Id,
            RowVersion = Convert.ToBase64String(project.RowVersion),
            PhotoVersion = photo.Version,
            Caption = photo.Caption,
            SetAsCover = CurrentCoverPhoto?.Id == photo.Id,
            LinkToTot = photo.TotId.HasValue
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, int photoId, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId || photoId != Input.PhotoId)
        {
            return BadRequest();
        }

        byte[]? rowVersionBytes = ParseRowVersion(Input.RowVersion);
        if (rowVersionBytes is null)
        {
            ModelState.AddModelError(string.Empty, "The form has expired. Please reload and try again.");
        }

        var crop = Input.ApplyCrop ? BuildCrop(Input) : null;
        if (Input.ApplyCrop && crop is null && HasPartialCrop(Input))
        {
            ModelState.AddModelError(string.Empty, "Crop requires X, Y, Width, and Height values.");
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .Include(p => p.Photos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!ProjectAccessGuard.CanManageProjectMedia(project, _userContext.User, userId))
        {
            return Forbid();
        }

        if (rowVersionBytes is not null && !project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            ModelState.AddModelError(string.Empty, "The project was updated by someone else. Please reload and try again.");
        }

        var photo = project.Photos.SingleOrDefault(p => p.Id == photoId);
        if (photo is null)
        {
            return NotFound();
        }

        Project = project;
        Photo = photo;
        CurrentCoverPhoto = ResolveCoverPhoto(project);
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);

        if (Input.PhotoVersion <= 0 || Input.PhotoVersion != photo.Version)
        {
            ModelState.AddModelError(string.Empty, "This photo was updated by someone else. Reload the page before making further changes.");
        }

        var tot = project.Tot;
        if (Input.LinkToTot && tot is null)
        {
            ModelState.AddModelError("Input.LinkToTot", "Transfer of Technology details have not been set up for this project yet.");
        }
        else if (Input.LinkToTot && tot?.Status == ProjectTotStatus.NotRequired)
        {
            ModelState.AddModelError("Input.LinkToTot", "Transfer of Technology is not required for this project.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await using var replacementStream = Input.File is not null && Input.File.Length > 0
                ? Input.File.OpenReadStream()
                : null;

            var desiredTotId = Input.LinkToTot ? project.Tot?.Id : null;
            var updated = await _photoService.UpdateAsync(
                project.Id,
                photo.Id,
                replacementStream,
                Input.File?.FileName,
                Input.File?.ContentType,
                crop,
                Input.Caption,
                Input.SetAsCover,
                desiredTotId,
                Input.PhotoVersion,
                userId,
                cancellationToken);

            if (updated is null)
            {
                return NotFound();
            }

            TempData["Flash"] = "Photo updated.";
            return RedirectToPage("./Index", new { id });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Photo {PhotoId} changed while being edited for project {ProjectId}", photoId, id);
            ModelState.AddModelError(string.Empty, "This photo was updated by someone else. Reload the page and try again.");
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Photo validation failed for photo {PhotoId}, project {ProjectId}", photoId, id);
            ModelState.AddModelError(string.Empty, FriendlyPhotoError(ex.Message));
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing photo {PhotoId} for project {ProjectId}", photoId, id);
            ModelState.AddModelError(string.Empty, "The photo could not be saved. Please try again.");
            return Page();
        }
    }

    private static ProjectPhoto? ResolveCoverPhoto(Project project)
    {
        if (project.CoverPhotoId.HasValue)
        {
            var explicitCover = project.Photos.FirstOrDefault(candidate => candidate.Id == project.CoverPhotoId.Value);
            if (explicitCover is not null)
            {
                return explicitCover;
            }
        }

        return project.Photos
            .Where(candidate => candidate.IsCover)
            .OrderBy(candidate => candidate.Ordinal)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
    }

    private static string FriendlyPhotoError(string? message)
    {
        var text = message ?? string.Empty;
        if (text.Contains("maximum size", StringComparison.OrdinalIgnoreCase))
        {
            return "The photo is too large. Choose a smaller file and try again.";
        }
        if (text.Contains("dimensions are too large", StringComparison.OrdinalIgnoreCase))
        {
            return "The photo dimensions are too large. Choose a smaller image and try again.";
        }
        if (text.Contains("crop", StringComparison.OrdinalIgnoreCase) || text.Contains("bounds", StringComparison.OrdinalIgnoreCase))
        {
            return "The selected crop could not be applied. Adjust the crop and try again.";
        }
        if (text.Contains("JPEG", StringComparison.OrdinalIgnoreCase) || text.Contains("supported image", StringComparison.OrdinalIgnoreCase))
        {
            return "Choose a JPEG, PNG or WebP image.";
        }
        if (text.Contains("Transfer of Technology", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }
        return "The photo could not be processed. Choose another image or try again.";
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

    private static ProjectPhotoCrop? BuildCrop(EditInput input)
    {
        if (input.CropX.HasValue && input.CropY.HasValue && input.CropWidth.HasValue && input.CropHeight.HasValue)
        {
            return new ProjectPhotoCrop(input.CropX.Value, input.CropY.Value, input.CropWidth.Value, input.CropHeight.Value);
        }

        return null;
    }

    private static bool HasPartialCrop(EditInput input)
    {
        var values = new[] { input.CropX, input.CropY, input.CropWidth, input.CropHeight };
        return values.Any(v => v.HasValue) && values.Any(v => !v.HasValue);
    }

    public class EditInput
    {
        public int ProjectId { get; set; }

        public int PhotoId { get; set; }

        public string RowVersion { get; set; } = string.Empty;

        public int PhotoVersion { get; set; }

        [StringLength(512)]
        public string? Caption { get; set; }

        public bool SetAsCover { get; set; }

        public bool LinkToTot { get; set; }

        public IFormFile? File { get; set; }

        public bool ApplyCrop { get; set; }

        public int? CropX { get; set; }

        public int? CropY { get; set; }

        public int? CropWidth { get; set; }

        public int? CropHeight { get; set; }
    }
}
