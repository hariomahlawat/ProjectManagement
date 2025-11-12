using System;
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
using ProjectManagement.Services.Documents;

namespace ProjectManagement.Pages.Projects.Documents;

[Authorize(Roles = "Admin,HoD,Project Officer")]
[AutoValidateAntiforgeryToken]
public class RetryOcrModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IDocumentService _documentService;
    private readonly ILogger<RetryOcrModel> _logger;

    public RetryOcrModel(
        ApplicationDbContext db,
        IUserContext userContext,
        IDocumentService documentService,
        ILogger<RetryOcrModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, int documentId, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageDocuments(project, userId))
        {
            return Forbid();
        }

        var documentExists = await _db.ProjectDocuments
            .AsNoTracking()
            .AnyAsync(d => d.ProjectId == id && d.Id == documentId, cancellationToken);
        if (!documentExists)
        {
            return NotFound();
        }

        try
        {
            await _documentService.RetryOcrAsync(documentId, userId, cancellationToken);
            TempData["Flash"] = "OCR re-queued. Check back in a few minutes.";
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            TempData["Error"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while re-queuing OCR for project document {DocumentId}", documentId);
            TempData["Error"] = "We couldn't re-queue OCR. Please try again.";
        }

        return RedirectToPage("../Overview", new { id });
    }

    // SECTION: Helpers
    private bool UserCanManageDocuments(Project project, string userId)
    {
        if (User.IsInRole("Admin"))
        {
            return true;
        }

        if (User.IsInRole("Project Officer") &&
            string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (User.IsInRole("HoD") &&
            string.Equals(project.HodUserId, userId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
