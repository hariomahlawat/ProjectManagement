using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.Projects.Documents;

[Authorize]
public class PreviewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;

    public PreviewModel(ApplicationDbContext db, IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public DocumentPreviewViewModel Document { get; private set; } = default!;

    public string ViewUrl { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int documentId, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var document = await _db.ProjectDocuments
            .AsNoTracking()
            .Include(d => d.Project)
            .Include(d => d.Stage)
            .Include(d => d.UploadedByUser)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null || document.Project is null)
        {
            return NotFound();
        }

        if (document.Status != ProjectDocumentStatus.Published || document.IsArchived)
        {
            return NotFound();
        }

        if (!ProjectAccessGuard.CanViewProject(document.Project, _userContext.User, userId))
        {
            return Forbid();
        }

        var tz = TimeZoneHelper.GetIst();
        var uploadedLocal = TimeZoneInfo.ConvertTime(document.UploadedAtUtc, tz);
        var uploaderName = !string.IsNullOrWhiteSpace(document.UploadedByUser?.FullName)
            ? document.UploadedByUser!.FullName
            : document.UploadedByUser?.UserName ?? "Unknown";

        Document = new DocumentPreviewViewModel
        {
            DocumentId = document.Id,
            ProjectId = document.ProjectId,
            Title = document.Title,
            StageDisplayName = document.Stage is null
                ? "General"
                : StageCodes.DisplayNameOf(document.Stage.StageCode),
            UploadedSummary = string.Format(
                CultureInfo.InvariantCulture,
                "Uploaded on {0:dd MMM yyyy, h:mm tt} IST by {1}",
                uploadedLocal,
                uploaderName),
            FileStamp = document.FileStamp,
            OriginalFileName = document.OriginalFileName
        };

        ViewData["Title"] = document.Title;
        ViewUrl = Url.Content($"~/Projects/Documents/View?documentId={document.Id}&t={document.FileStamp}");

        return Page();
    }

    public sealed record DocumentPreviewViewModel
    {
        public int DocumentId { get; init; }
        public int ProjectId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string StageDisplayName { get; init; } = string.Empty;
        public string UploadedSummary { get; init; } = string.Empty;
        public int FileStamp { get; init; }
        public string OriginalFileName { get; init; } = string.Empty;
    }
}
