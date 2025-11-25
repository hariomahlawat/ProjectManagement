using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Documents;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.Projects.Documents.Approvals;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public sealed class ReviewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IDocumentDecisionService _decisionService;
    private readonly ILogger<ReviewModel> _logger;

    public ReviewModel(
        ApplicationDbContext db,
        IUserContext userContext,
        IDocumentDecisionService decisionService,
        ILogger<ReviewModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    public ProjectSummaryViewModel Project { get; private set; } = default!;

    public RequestDetailViewModel RequestDetail { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id, int requestId, CancellationToken cancellationToken)
    {
        var projectResult = await EnsureProjectAccessAsync(id, cancellationToken);
        if (projectResult is not null)
        {
            return projectResult;
        }

        if (!await TryPopulateRequestAsync(id, requestId, cancellationToken))
        {
            TempData["Error"] = "Document request could not be found.";
            return RedirectToPage("./Index", new { id });
        }

        ViewData["Title"] = string.Format(CultureInfo.InvariantCulture, "Review document request – {0}", Project.Name);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, int requestId, CancellationToken cancellationToken)
    {
        return await DecideAsync(id, requestId, DecisionAction.Approve, cancellationToken);
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, int requestId, CancellationToken cancellationToken)
    {
        return await DecideAsync(id, requestId, DecisionAction.Reject, cancellationToken);
    }

    // SECTION: Decision handling pipeline
    private async Task<IActionResult> DecideAsync(int projectId, int requestId, DecisionAction action, CancellationToken cancellationToken)
    {
        if (requestId != Input.RequestId)
        {
            return BadRequest();
        }

        var projectResult = await EnsureProjectAccessAsync(projectId, cancellationToken);
        if (projectResult is not null)
        {
            return projectResult;
        }

        Input.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();

        if (!ModelState.IsValid)
        {
            if (await TryPopulateRequestAsync(projectId, requestId, cancellationToken))
            {
                ViewData["Title"] = string.Format(CultureInfo.InvariantCulture, "Review document request – {0}", Project.Name);
                return Page();
            }

            TempData["Error"] = "Document request could not be found.";
            return RedirectToPage("./Index", new { id = projectId });
        }

        if (!TryDecodeRowVersion(Input.RowVersion, out var expectedRowVersion))
        {
            ModelState.AddModelError(string.Empty, "We couldn't verify the request. Reload the page and try again.");
            if (await TryPopulateRequestAsync(projectId, requestId, cancellationToken))
            {
                ViewData["Title"] = string.Format(CultureInfo.InvariantCulture, "Review document request – {0}", Project.Name);
                return Page();
            }

            TempData["Error"] = "Document request could not be found.";
            return RedirectToPage("./Index", new { id = projectId });
        }

        var entity = await FetchRequestAsync(projectId, requestId, cancellationToken);
        if (entity is null)
        {
            TempData["Error"] = "Document request could not be found.";
            return RedirectToPage("./Index", new { id = projectId });
        }

        if (entity.Status != ProjectDocumentRequestStatus.Submitted)
        {
            TempData["Flash"] = "This request has already been processed.";
            return RedirectToPage("./Index", new { id = projectId });
        }

        if (!expectedRowVersion.AsSpan().SequenceEqual(entity.RowVersion))
        {
            TempData["Error"] = "This request was updated by someone else. Refresh the list and try again.";
            return RedirectToPage("./Index", new { id = projectId });
        }

        PopulateViewModel(entity);

        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        try
        {
            switch (action)
            {
                case DecisionAction.Approve:
                    await _decisionService.ApproveAsync(entity.Id, userId, Input.Note, cancellationToken);
                    TempData["Flash"] = GetApproveMessage(entity.RequestType);
                    break;
                case DecisionAction.Reject:
                    await _decisionService.RejectAsync(entity.Id, userId, Input.Note, cancellationToken);
                    TempData["Flash"] = "Document request rejected.";
                    break;
                default:
                    TempData["Error"] = "Unsupported action.";
                    return RedirectToPage("./Index", new { id = projectId });
            }

            return RedirectToPage("./Index", new { id = projectId });
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = "This request has already been processed.";
            return RedirectToPage("./Index", new { id = projectId });
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(
                ex,
                "Missing staged document for request {RequestId} in project {ProjectId} at {FilePath}",
                entity.Id,
                projectId,
                ex.FileName ?? "<unknown>");

            TempData["Error"] = "The staged file for this request is missing. Ensure the upload storage is shared across servers.";
            return RedirectToPage("./Index", new { id = projectId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("./Index", new { id = projectId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deciding document request {RequestId} for project {ProjectId}", entity.Id, projectId);
            ModelState.AddModelError(string.Empty, "We couldn't complete the action. Please try again.");
            ViewData["Title"] = string.Format(CultureInfo.InvariantCulture, "Review document request – {0}", Project.Name);
            return Page();
        }
    }

    private async Task<IActionResult?> EnsureProjectAccessAsync(int projectId, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.HodUserId })
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var principal = _userContext.User;
        var authorised = principal.IsInRole("Admin") ||
            (principal.IsInRole("HoD") && string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase));

        if (!authorised)
        {
            return Forbid();
        }

        Project = new ProjectSummaryViewModel(project.Id, project.Name);
        return null;
    }

    private async Task<ProjectDocumentRequest?> FetchRequestAsync(int projectId, int requestId, CancellationToken cancellationToken)
    {
        return await _db.ProjectDocumentRequests
            .AsNoTracking()
            .Include(r => r.Stage)
            .Include(r => r.Document)
            .ThenInclude(d => d!.UploadedByUser)
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == requestId, cancellationToken);
    }

    private async Task<bool> TryPopulateRequestAsync(int projectId, int requestId, CancellationToken cancellationToken)
    {
        var entity = await FetchRequestAsync(projectId, requestId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        PopulateViewModel(entity);
        return true;
    }

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            rowVersion = Array.Empty<byte>();
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            rowVersion = Array.Empty<byte>();
            return false;
        }
    }

    private void PopulateViewModel(ProjectDocumentRequest request)
    {
        var ist = TimeZoneHelper.GetIst();
        var requestedAtLocal = TimeZoneInfo.ConvertTime(request.RequestedAtUtc, ist);
        var stageDisplay = request.Stage is null
            ? "General"
            : StageCodes.DisplayNameOf(request.Stage.StageCode);

        var requestedBy = request.RequestedByUser is null
            ? "Unknown"
            : (string.IsNullOrWhiteSpace(request.RequestedByUser.FullName)
                ? (request.RequestedByUser.UserName ?? request.RequestedByUser.Email ?? "Unknown")
                : request.RequestedByUser.FullName);

        DocumentSummaryViewModel? documentSummary = null;
        if (request.Document is not null)
        {
            var uploadedAtLocal = TimeZoneInfo.ConvertTime(request.Document.UploadedAtUtc, ist);
            var uploadedBy = request.Document.UploadedByUser is null
                ? "Unknown"
                : (string.IsNullOrWhiteSpace(request.Document.UploadedByUser.FullName)
                    ? (request.Document.UploadedByUser.UserName ?? request.Document.UploadedByUser.Email ?? "Unknown")
                    : request.Document.UploadedByUser.FullName);

            documentSummary = new DocumentSummaryViewModel(
                request.Document.Id,
                request.Document.Title,
                request.Document.OriginalFileName,
                request.Document.FileSize,
                request.Document.FileStamp,
                uploadedBy,
                uploadedAtLocal,
                request.Document.Status == ProjectDocumentStatus.SoftDeleted);
        }

        RequestDetail = new RequestDetailViewModel(
            request.Id,
            stageDisplay,
            request.Stage?.StageCode ?? string.Empty,
            request.Title,
            DescribeAction(request.RequestType),
            requestedBy,
            requestedAtLocal,
            request.OriginalFileName,
            request.FileSize,
            request.ContentType,
            request.Description,
            documentSummary,
            request.RequestType);

        Input.RequestId = request.Id;
        Input.RowVersion = Convert.ToBase64String(request.RowVersion);
    }

    private static string DescribeAction(ProjectDocumentRequestType type) => type switch
    {
        ProjectDocumentRequestType.Upload => "Publish new document",
        ProjectDocumentRequestType.Replace => "Overwrite existing document",
        ProjectDocumentRequestType.Delete => "Remove document",
        _ => type.ToString()
    };

    private static string GetApproveMessage(ProjectDocumentRequestType type) => type switch
    {
        ProjectDocumentRequestType.Upload => "Document published.",
        ProjectDocumentRequestType.Replace => "Document replaced with the new file.",
        ProjectDocumentRequestType.Delete => "Document removed from the project.",
        _ => "Document request approved."
    };

    public sealed record ProjectSummaryViewModel(int Id, string Name);

    public sealed record RequestDetailViewModel(
        int RequestId,
        string Stage,
        string StageCode,
        string Title,
        string Action,
        string RequestedBy,
        DateTimeOffset RequestedAtLocal,
        string? OriginalFileName,
        long? FileSize,
        string? ContentType,
        string? Description,
        DocumentSummaryViewModel? Document,
        ProjectDocumentRequestType RequestType);

    public sealed record DocumentSummaryViewModel(
        int DocumentId,
        string Title,
        string OriginalFileName,
        long FileSize,
        int FileStamp,
        string UploadedBy,
        DateTimeOffset UploadedAtLocal,
        bool IsArchived);

    public sealed class DecisionInput
    {
        [Required]
        public int RequestId { get; set; }

        [StringLength(2000)]
        public string? Note { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    private enum DecisionAction
    {
        Approve,
        Reject
    }
}
