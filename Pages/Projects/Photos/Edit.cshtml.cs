using System;
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

    public async Task<IActionResult> OnGetAsync(int id, int photoId, CancellationToken cancellationToken)
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

        var photo = project.Photos.SingleOrDefault(p => p.Id == photoId);
        if (photo is null)
        {
            return NotFound();
        }

        Project = project;
        Photo = photo;
        Input = new EditInput
        {
            ProjectId = project.Id,
            PhotoId = photo.Id,
            RowVersion = Convert.ToBase64String(project.RowVersion),
            Caption = photo.Caption,
            SetAsCover = photo.IsCover
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

        var crop = BuildCrop(Input);
        if (crop is null && HasPartialCrop(Input))
        {
            ModelState.AddModelError(string.Empty, "Crop requires X, Y, Width, and Height values.");
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
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var hasChanges = false;

        try
        {
            if (Input.File is not null && Input.File.Length > 0)
            {
                await using var stream = Input.File.OpenReadStream();
                if (crop.HasValue)
                {
                    var replaced = await _photoService.ReplaceAsync(project.Id,
                        photo.Id,
                        stream,
                        Input.File.FileName,
                        Input.File.ContentType,
                        userId,
                        crop.Value,
                        cancellationToken);
                    hasChanges = hasChanges || replaced is not null;
                }
                else
                {
                    var replaced = await _photoService.ReplaceAsync(project.Id,
                        photo.Id,
                        stream,
                        Input.File.FileName,
                        Input.File.ContentType,
                        userId,
                        cancellationToken);
                    hasChanges = hasChanges || replaced is not null;
                }
            }
            else if (crop.HasValue)
            {
                var updated = await _photoService.UpdateCropAsync(project.Id, photo.Id, crop.Value, userId, cancellationToken);
                hasChanges = hasChanges || updated is not null;
            }

            if (!string.Equals(photo.Caption ?? string.Empty, Input.Caption ?? string.Empty, StringComparison.Ordinal))
            {
                var updated = await _photoService.UpdateCaptionAsync(project.Id, photo.Id, Input.Caption, userId, cancellationToken);
                hasChanges = hasChanges || updated is not null;
            }

            if (Input.SetAsCover && !photo.IsCover)
            {
                foreach (var other in project.Photos.Where(p => p.IsCover && p.Id != photo.Id))
                {
                    other.IsCover = false;
                }

                photo.IsCover = true;
                project.CoverPhotoId = photo.Id;
                project.CoverPhotoVersion = photo.Version;
                await _db.SaveChangesAsync(cancellationToken);
                hasChanges = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing photo {PhotoId} for project {ProjectId}", photoId, id);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        if (!hasChanges)
        {
            TempData["Flash"] = "No changes were made.";
            return RedirectToPage("./Index", new { id });
        }

        TempData["Flash"] = "Photo updated.";
        return RedirectToPage("./Index", new { id });
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

        public string? Caption { get; set; }

        public bool SetAsCover { get; set; }

        public IFormFile? File { get; set; }

        public int? CropX { get; set; }

        public int? CropY { get; set; }

        public int? CropWidth { get; set; }

        public int? CropHeight { get; set; }
    }
}
