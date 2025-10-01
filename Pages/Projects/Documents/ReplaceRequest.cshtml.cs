using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
public class ReplaceRequestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IDocumentService _documentService;
    private readonly IDocumentRequestService _requestService;
    private readonly ProjectDocumentOptions _options;
    private readonly ILogger<ReplaceRequestModel> _logger;

    public ReplaceRequestModel(
        ApplicationDbContext db,
        IUserContext userContext,
        IDocumentService documentService,
        IDocumentRequestService requestService,
        IOptions<ProjectDocumentOptions> options,
        ILogger<ReplaceRequestModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public ReplaceInputModel Input { get; set; } = new();

    public Project? Project { get; private set; }

    public string DocumentTitle { get; private set; } = string.Empty;

    public string DocumentStage { get; private set; } = string.Empty;

    public string DocumentFileName { get; private set; } = string.Empty;

    public long MaxFileSizeBytes => (long)_options.MaxSizeMb * 1024L * 1024L;

    public IReadOnlyCollection<string> AllowedContentTypes => _options.AllowedMimeTypes.ToList();

    public async Task<IActionResult> OnGetAsync(int id, int documentId, CancellationToken cancellationToken)
    {
        var accessResult = await EnsureProjectAccessAsync(id, cancellationToken);
        if (accessResult is not null)
        {
            return accessResult;
        }

        var documentResult = await LoadDocumentAsync(id, documentId, cancellationToken);
        if (documentResult is not null)
        {
            return documentResult;
        }

        Input.ProjectId = id;
        Input.DocumentId = documentId;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, int documentId, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId || documentId != Input.DocumentId)
        {
            return BadRequest();
        }

        var accessResult = await EnsureProjectAccessAsync(id, cancellationToken);
        if (accessResult is not null)
        {
            return accessResult;
        }

        var documentResult = await LoadDocumentAsync(id, documentId, cancellationToken);
        if (documentResult is not null)
        {
            return documentResult;
        }

        Input.Nomenclature = string.IsNullOrWhiteSpace(Input.Nomenclature)
            ? null
            : Input.Nomenclature.Trim();

        if (Input.File is null)
        {
            ModelState.AddModelError("Input.File", "Select a PDF file to upload.");
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

            await _requestService.CreateReplaceRequestAsync(
                Input.DocumentId,
                Input.Nomenclature,
                tempFile,
                userId,
                cancellationToken);

            TempData["Flash"] = "Document replacement request submitted for moderation.";
            return RedirectToPage("../Overview", new { id = Input.ProjectId });
        }
        catch (InvalidOperationException ex)
        {
            if (tempFile is null)
            {
                _logger.LogWarning(ex, "Validation failed while staging replacement for document {DocumentId}", Input.DocumentId);
                ModelState.AddModelError("Input.File", ex.Message);
            }
            else
            {
                if (ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "Document {DocumentId} disappeared while creating replacement request", Input.DocumentId);
                    return NotFound();
                }

                var message = ex.Message.Contains("pending request", StringComparison.OrdinalIgnoreCase)
                    ? "A pending request already exists for this document. Please wait for moderation to finish."
                    : ex.Message;
                _logger.LogWarning(ex, "Could not create replacement request for document {DocumentId}", Input.DocumentId);
                ModelState.AddModelError(string.Empty, message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while staging replacement for document {DocumentId}", Input.DocumentId);
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

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
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

    private async Task<IActionResult?> LoadDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken)
    {
        var document = await _db.ProjectDocuments
            .Include(d => d.Stage)
            .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.Id == documentId, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        if (document.Status != ProjectDocumentStatus.Published || document.IsArchived)
        {
            return NotFound();
        }

        DocumentTitle = document.Title;
        DocumentStage = document.Stage is null
            ? "General"
            : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", StageCodes.DisplayNameOf(document.Stage.StageCode), document.Stage.StageCode);
        DocumentFileName = document.OriginalFileName;

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

    public sealed class ReplaceInputModel
    {
        [Required]
        public int ProjectId { get; set; }

        [Required]
        public int DocumentId { get; set; }

        [MaxLength(200)]
        public string? Nomenclature { get; set; }

        [Required]
        public IFormFile? File { get; set; }
    }
}
