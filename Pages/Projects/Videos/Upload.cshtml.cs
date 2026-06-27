using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ProjectVideoOptions _options;
    private readonly ILogger<UploadModel> _logger;

    public UploadModel(
        ApplicationDbContext db,
        IUserContext userContext,
        IProjectVideoService videoService,
        IOptions<ProjectVideoOptions> options,
        ILogger<UploadModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public UploadInput Input { get; set; } = new();

    public Project Project { get; private set; } = null!;
    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var projectResult = await LoadManagedProjectAsync(id, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        Project = projectResult.Project!;
        Input.ProjectId = Project.Id;
        Input.RowVersion = Convert.ToBase64String(Project.RowVersion);
        Input.SetAsFeatured = Project.FeaturedVideoId is null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        var projectResult = await LoadManagedProjectAsync(id, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        Project = projectResult.Project!;
        var expectedVersion = ParseRowVersion(Input.RowVersion);
        Input.RowVersion = Convert.ToBase64String(Project.RowVersion);

        if (expectedVersion is null || !Project.RowVersion.AsSpan().SequenceEqual(expectedVersion))
        {
            ModelState.AddModelError(string.Empty, "The project changed while you were working. Reload this page and try again.");
        }

        if (Input.File is null || Input.File.Length == 0)
        {
            ModelState.AddModelError("Input.File", "Choose a video to upload.");
        }
        else if (_options.MaxFileSizeBytes > 0 && Input.File.Length > _options.MaxFileSizeBytes)
        {
            ModelState.AddModelError("Input.File", $"Choose a video smaller than {FormatSize(_options.MaxFileSizeBytes)}.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await using var stream = Input.File!.OpenReadStream();
            await _videoService.AddAsync(
                Project.Id,
                stream,
                Input.File.FileName,
                Input.File.ContentType,
                _userContext.UserId!,
                Input.Title,
                Input.Description,
                Input.SetAsFeatured,
                cancellationToken);

            TempData["Flash"] = "Video uploaded.";
            return RedirectToPage("/Projects/Videos/Index", new { id = Project.Id });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Video upload validation failed for project {ProjectId}", id);
            ModelState.AddModelError("Input.File", FriendlyUploadError(ex));
            return Page();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Video storage failed for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty, "The video could not be saved. Please try again.");
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected video upload error for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty, "The video could not be uploaded. Please try again.");
            return Page();
        }
    }

    private async Task<(Project? Project, IActionResult? Result)> LoadManagedProjectAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_userContext.UserId))
        {
            return (null, Forbid());
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

        return (project, null);
    }

    private string FriendlyUploadError(InvalidOperationException exception)
    {
        var message = exception.Message;
        if (message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exceeds", StringComparison.OrdinalIgnoreCase))
        {
            return $"Choose a video smaller than {FormatSize(_options.MaxFileSizeBytes)}.";
        }

        if (message.Contains("content type", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("supported", StringComparison.OrdinalIgnoreCase))
        {
            return "Choose an MP4, WebM or OGG video.";
        }

        return "This video could not be processed. Choose another file and try again.";
    }

    private static string FormatSize(long bytes)
    {
        var megabytes = bytes / (1024d * 1024d);
        return megabytes >= 1024
            ? $"{megabytes / 1024d:0.#} GB"
            : $"{megabytes:0} MB";
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
        public bool SetAsFeatured { get; set; }
    }
}
