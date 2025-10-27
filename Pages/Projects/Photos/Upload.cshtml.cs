using System;
using System.Collections.Generic;
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
public class UploadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectPhotoService _photoService;
    private readonly ILogger<UploadModel> _logger;

    public UploadModel(ApplicationDbContext db,
                       IUserContext userContext,
                       IProjectPhotoService photoService,
                       ILogger<UploadModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public UploadInput Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public bool AllowTotLinking => Project?.Tot is { Status: not ProjectTotStatus.NotRequired };

    public string TotStatusDisplay => Project?.Tot?.Status switch
    {
        ProjectTotStatus.NotRequired => "Not required",
        ProjectTotStatus.NotStarted => "Not started",
        ProjectTotStatus.InProgress => "In progress",
        ProjectTotStatus.Completed => "Completed",
        _ => "Unknown"
    };

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
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
        Input.ProjectId = project.Id;
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);
        Input.LinkToTot = false;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        byte[]? rowVersionBytes = ParseRowVersion(Input.RowVersion);
        if (rowVersionBytes is null)
        {
            ModelState.AddModelError(string.Empty, "The form has expired. Please reload and try again.");
        }

        var files = (Input.Files ?? new List<IFormFile>()).Where(f => f is { Length: > 0 }).ToList();
        if (files.Count == 0)
        {
            ModelState.AddModelError("Input.Files", "Please select at least one photo to upload.");
        }

        var isMulti = files.Count > 1;
        ProjectPhotoCrop? crop = null;
        if (!isMulti)
        {
            crop = BuildCrop(Input);
            if (crop is null && HasPartialCrop(Input))
            {
                ModelState.AddModelError(string.Empty, "Crop requires X, Y, Width, and Height values.");
            }
        }
        else
        {
            Input.CropX = Input.CropY = Input.CropWidth = Input.CropHeight = null;
            Input.Caption = null;
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
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
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);

        var tot = project.Tot;
        var canLinkTot = tot is not null && tot.Status != ProjectTotStatus.NotRequired;
        if (Input.LinkToTot && !canLinkTot)
        {
            ModelState.AddModelError("Input.LinkToTot", "Transfer of Technology is not required for this project.");
        }
        else if (Input.LinkToTot && tot is null)
        {
            ModelState.AddModelError("Input.LinkToTot", "Transfer of Technology details have not been set up for this project yet.");
        }

        if (rowVersionBytes is not null && !project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            ModelState.AddModelError(string.Empty, "The project was updated by someone else. Please reload and try again.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            if (isMulti)
            {
                var first = true;
                foreach (var file in files)
                {
                    await using var stream = file.OpenReadStream();
                    await _photoService.AddAsync(project.Id,
                        stream,
                        file.FileName,
                        file.ContentType,
                        userId,
                        Input.SetAsCover && first,
                        null,
                        Input.LinkToTot ? project.Tot!.Id : (int?)null,
                        cancellationToken);
                    first = false;
                }

                TempData["Flash"] = $"{files.Count} photos uploaded.";
            }
            else
            {
                var file = files[0];
                await using var stream = file.OpenReadStream();
                if (crop.HasValue)
                {
                    await _photoService.AddAsync(project.Id,
                        stream,
                        file.FileName,
                        file.ContentType,
                        userId,
                        Input.SetAsCover,
                        Input.Caption,
                        crop.Value,
                        Input.LinkToTot ? project.Tot!.Id : (int?)null,
                        cancellationToken);
                }
                else
                {
                    await _photoService.AddAsync(project.Id,
                        stream,
                        file.FileName,
                        file.ContentType,
                        userId,
                        Input.SetAsCover,
                        Input.Caption,
                        Input.LinkToTot ? project.Tot!.Id : (int?)null,
                        cancellationToken);
                }

                TempData["Flash"] = "Photo uploaded.";
            }

            return RedirectToPage("./Index", new { id });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid crop selection while uploading photo for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty,
                "We couldn't process that crop. Keep the crop box inside the image, maintain the 4:3 ratio, and try again.");
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading photo for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostBatchAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageProject(project, userId))
        {
            return Forbid();
        }

        var files = Request.Form.Files;
        if (files.Count == 0)
        {
            return BadRequest(new { success = false, message = "Select a photo to upload." });
        }

        var setAsCover = TryParseBool(Request.Form["Input.SetAsCover"]);
        var linkToTot = TryParseBool(Request.Form["Input.LinkToTot"]);
        var caption = Request.Form["Input.Caption"].ToString();
        var allowCaption = files.Count == 1;

        var rowVersionValue = Request.Form["Input.RowVersion"].ToString();
        var rowVersionBytes = ParseRowVersion(rowVersionValue);
        if (rowVersionBytes is not null && !project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            return Conflict(new { success = false, message = "The project changed while uploading. Reload and try again." });
        }

        if (linkToTot)
        {
            if (project.Tot is null)
            {
                return BadRequest(new { success = false, message = "Transfer of Technology is not configured for this project." });
            }

            if (project.Tot.Status == ProjectTotStatus.NotRequired)
            {
                return BadRequest(new { success = false, message = "Transfer of Technology is not required for this project." });
            }
        }

        var responses = new List<object>();
        var allSucceeded = true;
        var first = true;

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                allSucceeded = false;
                responses.Add(new { name = file.FileName, success = false, error = "File is empty." });
                continue;
            }

            try
            {
                await using var stream = file.OpenReadStream();
                await _photoService.AddAsync(project.Id,
                    stream,
                    file.FileName,
                    file.ContentType,
                    userId,
                    setAsCover && first,
                    allowCaption ? caption : null,
                    linkToTot ? project.Tot?.Id : null,
                    cancellationToken);
                responses.Add(new { name = file.FileName, success = true });
                first = false;
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                _logger.LogError(ex, "Error during batch photo upload for project {ProjectId}", id);
                responses.Add(new { name = file.FileName, success = false, error = ex.Message });
            }
        }

        await _db.Entry(project).ReloadAsync(cancellationToken);

        return new JsonResult(new
        {
            success = allSucceeded,
            items = responses,
            rowVersion = Convert.ToBase64String(project.RowVersion)
        });
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

    private static ProjectPhotoCrop? BuildCrop(UploadInput input)
    {
        if (input.CropX.HasValue && input.CropY.HasValue && input.CropWidth.HasValue && input.CropHeight.HasValue)
        {
            return new ProjectPhotoCrop(input.CropX.Value, input.CropY.Value, input.CropWidth.Value, input.CropHeight.Value);
        }

        return null;
    }

    private static bool HasPartialCrop(UploadInput input)
    {
        var values = new[] { input.CropX, input.CropY, input.CropWidth, input.CropHeight };
        return values.Any(v => v.HasValue) && values.Any(v => !v.HasValue);
    }

    private static bool TryParseBool(string? value) => bool.TryParse(value, out var result) && result;

    public class UploadInput
    {
        public int ProjectId { get; set; }

        public string RowVersion { get; set; } = string.Empty;

        public List<IFormFile> Files { get; set; } = new();

        public string? Caption { get; set; }

        public bool SetAsCover { get; set; }

        public bool LinkToTot { get; set; }

        public int? CropX { get; set; }

        public int? CropY { get; set; }

        public int? CropWidth { get; set; }

        public int? CropHeight { get; set; }
    }
}
