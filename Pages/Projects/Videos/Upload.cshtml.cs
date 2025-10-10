using System;
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

namespace ProjectManagement.Pages.Projects.Videos;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public sealed class UploadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectVideoService _videoService;
    private readonly ILogger<UploadModel> _logger;

    public UploadModel(ApplicationDbContext db,
                       IUserContext userContext,
                       IProjectVideoService videoService,
                       ILogger<UploadModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
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
        Input.SetAsFeatured = project.FeaturedVideoId is null;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        var rowVersionBytes = ParseRowVersion(Input.RowVersion);
        if (rowVersionBytes is null)
        {
            ModelState.AddModelError(string.Empty, "The form has expired. Please reload and try again.");
        }

        if (Input.File is null || Input.File.Length == 0)
        {
            ModelState.AddModelError("Input.File", "Please select a file to upload.");
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
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
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);

        if (rowVersionBytes is not null && !project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            ModelState.AddModelError(string.Empty, "The project was updated by someone else. Please reload and try again.");
        }

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

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await using var stream = Input.File!.OpenReadStream();
            await _videoService.AddAsync(
                project.Id,
                stream,
                Input.File.FileName,
                Input.File.ContentType,
                userId,
                Input.Title,
                Input.Description,
                Input.LinkToTot ? project.Tot?.Id : (int?)null,
                Input.SetAsFeatured,
                cancellationToken);

            TempData["Flash"] = "Video uploaded.";
            return RedirectToPage("/Projects/Videos/Index", new { id = project.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading video for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
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

    public sealed class UploadInput
    {
        public int ProjectId { get; set; }

        public string RowVersion { get; set; } = string.Empty;

        public IFormFile? File { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public bool LinkToTot { get; set; }

        public bool SetAsFeatured { get; set; }
    }
}
