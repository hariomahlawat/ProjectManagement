using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Documents;

namespace ProjectManagement.Pages.Projects.Documents;

[Authorize(Roles = "Admin,HoD,Project Officer")]
[AutoValidateAntiforgeryToken]
public class UploadRequestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IDocumentService _documentService;
    private readonly IDocumentRequestService _requestService;
    private readonly ProjectDocumentOptions _options;
    private readonly ILogger<UploadRequestModel> _logger;

    public UploadRequestModel(
        ApplicationDbContext db,
        IUserContext userContext,
        IDocumentService documentService,
        IDocumentRequestService requestService,
        IOptions<ProjectDocumentOptions> options,
        ILogger<UploadRequestModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public UploadInputModel Input { get; set; } = new();

    public Project? Project { get; private set; }

    public IEnumerable<SelectListItem> StageOptions { get; private set; } = Array.Empty<SelectListItem>();

    public bool HasStageOptions { get; private set; }

    public long MaxFileSizeBytes => (long)_options.MaxSizeMb * 1024L * 1024L;

    public IReadOnlyCollection<string> AllowedContentTypes => _options.AllowedMimeTypes.ToList();

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
        var result = await EnsureProjectAccessAsync(id, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        StageOptions = await BuildStageOptionsAsync(id, cancellationToken);
        if (!HasStageOptions)
        {
            Input.StageId = null;
        }
        Input.ProjectId = id;
        Input.LinkToTot = false;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        var result = await EnsureProjectAccessAsync(id, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        StageOptions = await BuildStageOptionsAsync(id, cancellationToken);

        if (!HasStageOptions)
        {
            Input.StageId = null;
        }
        else if (Input.StageId is null)
        {
            ModelState.AddModelError("Input.StageId", "Select a stage.");
        }
        else
        {
            var stageExists = await _db.ProjectStages
                .AnyAsync(s => s.ProjectId == id && s.Id == Input.StageId, cancellationToken);
            if (!stageExists)
            {
                ModelState.AddModelError("Input.StageId", "Select a valid stage.");
            }
        }

        if (Input.File is null)
        {
            ModelState.AddModelError("Input.File", "Select a PDF file to upload.");
        }

        Input.Nomenclature = Input.Nomenclature?.Trim() ?? string.Empty;

        var tot = Project?.Tot;
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

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        DocumentFileDescriptor? tempFile = null;
        var token = _documentService.CreateTempRequestToken();

        try
        {
            await using var stream = Input.File!.OpenReadStream();
            tempFile = await _documentService.SaveTempAsync(
                token,
                stream,
                Input.File.FileName,
                Input.File.ContentType,
                cancellationToken);

            await _requestService.CreateUploadRequestAsync(
                Input.ProjectId,
                Input.StageId,
                Input.Nomenclature,
                Input.LinkToTot ? Project!.Tot!.Id : (int?)null,
                tempFile,
                userId,
                cancellationToken);

            TempData["Flash"] = "Document upload request submitted for moderation.";
            return RedirectToPage("../Overview", new { id = Input.ProjectId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Validation failed while staging upload request for project {ProjectId}", Input.ProjectId);
            ModelState.AddModelError("Input.File", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while staging upload request for project {ProjectId}", Input.ProjectId);
            ModelState.AddModelError(string.Empty, "We couldn't process the file. Please try again.");
        }
        finally
        {
            if (!ModelState.IsValid && tempFile is not null)
            {
                await _documentService.DeleteTempAsync(tempFile.StorageKey, cancellationToken);
            }
        }

        return Page();
    }

    private async Task<IActionResult?> EnsureProjectAccessAsync(int projectId, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanSubmitRequests(project, userId))
        {
            return Forbid();
        }

        Project = project;
        return null;
    }

    private bool UserCanSubmitRequests(Project project, string userId)
    {
        var principal = _userContext.User;
        if (principal.IsInRole("Admin"))
        {
            return true;
        }

        if (principal.IsInRole("Project Officer") &&
            string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return principal.IsInRole("HoD") &&
            string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IEnumerable<SelectListItem>> BuildStageOptionsAsync(int projectId, CancellationToken cancellationToken)
    {
        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.StageCode })
            .ToListAsync(cancellationToken);

        HasStageOptions = stages.Count > 0;

        return stages
            .Select(stage => new SelectListItem(
                string.Format(CultureInfo.InvariantCulture, "{0} ({1})", StageCodes.DisplayNameOf(stage.StageCode), stage.StageCode),
                stage.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    public sealed class UploadInputModel
    {
        [Required]
        public int ProjectId { get; set; }

        public int? StageId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Nomenclature { get; set; } = string.Empty;

        [Required]
        public IFormFile? File { get; set; }

        public bool LinkToTot { get; set; }
    }
}
