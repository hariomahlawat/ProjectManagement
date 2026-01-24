using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    // SECTION: Constants
    private const int MaxNomenclatureLength = 200;
    private const string FilesFieldKey = "Input.Files";
    private const string StageFieldKey = "Input.StageId";

    // SECTION: Dependencies
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

    // SECTION: Upload constraints
    public long MaxFileSizeBytes => (long)_options.MaxSizeMb * 1024L * 1024L;

    public IReadOnlyCollection<string> AllowedContentTypes => _options.AllowedMimeTypes.ToList();

    public IReadOnlyCollection<string> AllowedExtensions => DocumentTypeValidation.GetAllowedExtensions(_options.AllowedMimeTypes);

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

        // SECTION: Stage validation (optional)
        if (!HasStageOptions)
        {
            Input.StageId = null;
        }
        else if (Input.StageId.HasValue)
        {
            var stageExists = await _db.ProjectStages
                .AnyAsync(s => s.ProjectId == id && s.Id == Input.StageId.Value, cancellationToken);
            if (!stageExists)
            {
                ModelState.AddModelError(StageFieldKey, "Select a valid stage.");
            }
        }

        // SECTION: Files validation
        var files = GetSelectedFiles(Input.Files);
        if (files.Count == 0)
        {
            ModelState.AddModelError(FilesFieldKey, "Select at least one file to upload.");
        }

        // SECTION: Nomenclature preparation (optional base value)
        var baseNomenclature = NormalizeBaseNomenclature(Input.Nomenclature);

        // SECTION: Transfer of Technology validation
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

        // SECTION: Multi-file processing
        var tempFiles = new List<DocumentFileDescriptor>(files.Count);
        var createdRequests = 0;

        try
        {
            foreach (var (file, index) in files.Select((value, i) => (value, i)))
            {
                var token = _documentService.CreateTempRequestToken();

                await using var stream = file.OpenReadStream();
                var tempFile = await _documentService.SaveTempAsync(
                    token,
                    stream,
                    file.FileName,
                    file.ContentType,
                    cancellationToken);

                tempFiles.Add(tempFile);

                var nomenclature = BuildPerFileNomenclature(baseNomenclature, files.Count, tempFile.OriginalFileName);

                await _requestService.CreateUploadRequestAsync(
                    Input.ProjectId,
                    Input.StageId,
                    nomenclature,
                    Input.LinkToTot ? Project!.Tot!.Id : (int?)null,
                    tempFile,
                    userId,
                    cancellationToken);

                createdRequests = index + 1;
            }

            TempData["Flash"] = files.Count == 1
                ? "Submitted 1 file for moderation."
                : FormattableString.Invariant($"Submitted {files.Count} file(s) for moderation.");

            return RedirectToPage("../Overview", new { id = Input.ProjectId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Validation failed while staging upload request for project {ProjectId}", Input.ProjectId);
            ModelState.AddModelError(FilesFieldKey, FormatFileErrorMessage(ex.Message, files, createdRequests));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File read failed while staging upload request for project {ProjectId}", Input.ProjectId);
            ModelState.AddModelError(FilesFieldKey, "We couldn't read the uploaded file. Please try again with a fresh upload.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while staging upload request for project {ProjectId}", Input.ProjectId);
            ModelState.AddModelError(string.Empty, "We couldn't process the files. Please try again.");
        }
        finally
        {
            if (!ModelState.IsValid)
            {
                foreach (var tempFile in tempFiles)
                {
                    await _documentService.DeleteTempAsync(tempFile.StorageKey, cancellationToken);
                }
            }
        }

        return Page();
    }

    // SECTION: Validation helpers
    private static List<IFormFile> GetSelectedFiles(IEnumerable<IFormFile>? files)
        => files?
            .Where(file => file is not null && file.Length > 0)
            .ToList()
            ?? new List<IFormFile>();

    private static string? NormalizeBaseNomenclature(string? nomenclature)
    {
        var trimmed = nomenclature?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string BuildPerFileNomenclature(string? baseNomenclature, int totalFiles, string originalFileName)
    {
        var fileBaseName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(fileBaseName))
        {
            fileBaseName = "document";
        }

        // SECTION: Single-file behaviour
        if (totalFiles == 1)
        {
            return TrimToLength(baseNomenclature ?? fileBaseName, MaxNomenclatureLength);
        }

        // SECTION: Multi-file behaviour
        if (string.IsNullOrWhiteSpace(baseNomenclature))
        {
            return TrimToLength(fileBaseName, MaxNomenclatureLength);
        }

        var combined = $"{baseNomenclature} - {fileBaseName}";
        return TrimToLength(combined, MaxNomenclatureLength);
    }

    private static string TrimToLength(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }

    private static string FormatFileErrorMessage(string message, IReadOnlyList<IFormFile> files, int createdRequests)
    {
        if (files.Count <= 1)
        {
            return message;
        }

        var failedIndex = Math.Clamp(createdRequests, 0, files.Count - 1);
        var failedFileName = files[failedIndex].FileName;
        var sanitizedMessage = string.IsNullOrWhiteSpace(message) ? "The upload could not be processed." : message.Trim();

        var builder = new StringBuilder();
        builder.Append("Upload failed for ");
        builder.Append(failedFileName);
        builder.Append('.');

        if (!sanitizedMessage.EndsWith('.', StringComparison.Ordinal))
        {
            builder.Append(' ');
            builder.Append(sanitizedMessage);
        }
        else
        {
            builder.Append(' ');
            builder.Append(sanitizedMessage.TrimEnd('.'));
            builder.Append('.');
        }

        return builder.ToString();
    }

    // SECTION: Access control
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

    // SECTION: Stage options
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

        [MaxLength(MaxNomenclatureLength)]
        public string? Nomenclature { get; set; }

        public List<IFormFile> Files { get; set; } = new();

        public bool LinkToTot { get; set; }
    }
}
