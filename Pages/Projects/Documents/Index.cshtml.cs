using System;
using System.Collections.Generic;
using System.Linq;
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

namespace ProjectManagement.Pages.Projects.Documents;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;

    public IndexModel(ApplicationDbContext db, IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public Project Project { get; private set; } = null!;
    public IReadOnlyList<DocumentItem> Documents { get; private set; } = Array.Empty<DocumentItem>();
    public IReadOnlyList<StageOption> Stages { get; private set; } = Array.Empty<StageOption>();
    public bool CanManage { get; private set; }
    public int PendingCount { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? StageId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (_userContext.User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!ProjectAccessGuard.CanViewProjectDocuments(project, _userContext.User))
        {
            return Forbid();
        }

        Project = project;
        CanManage = ProjectAccessGuard.CanManageProjectDocuments(project, _userContext.User, _userContext.UserId);

        Stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == id)
            .OrderBy(stage => stage.SortOrder)
            .Select(stage => new StageOption(stage.Id, StageCodes.DisplayNameOf(stage.StageCode)))
            .ToListAsync(cancellationToken);

        var query = _db.ProjectDocuments
            .AsNoTracking()
            .Include(document => document.Stage)
            .Include(document => document.UploadedByUser)
            .Where(document => document.ProjectId == id &&
                               document.Status == ProjectDocumentStatus.Published &&
                               !document.IsArchived);

        if (StageId.HasValue)
        {
            query = query.Where(document => document.StageId == StageId.Value);
        }

        var search = Q?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(document =>
                EF.Functions.ILike(document.Title, $"%{search}%") ||
                EF.Functions.ILike(document.OriginalFileName, $"%{search}%"));
        }

        Documents = await query
            .OrderByDescending(document => document.UploadedAtUtc)
            .ThenBy(document => document.Title)
            .Select(document => new DocumentItem(
                document.Id,
                document.Title,
                document.OriginalFileName,
                document.FileSize,
                document.Stage == null ? "General" : StageCodes.DisplayNameOf(document.Stage.StageCode),
                document.UploadedAtUtc,
                document.UploadedByUser != null
                    ? (document.UploadedByUser.FullName ?? document.UploadedByUser.UserName ?? "Unknown")
                    : "Unknown"))
            .ToListAsync(cancellationToken);

        if (CanManage)
        {
            PendingCount = await _db.ProjectDocumentRequests
                .AsNoTracking()
                .CountAsync(request => request.ProjectId == id &&
                    (request.Status == ProjectDocumentRequestStatus.Draft || request.Status == ProjectDocumentRequestStatus.Submitted), cancellationToken);
        }

        return Page();
    }

    public sealed record StageOption(int Id, string Name);

    public sealed record DocumentItem(
        int Id,
        string Title,
        string OriginalFileName,
        long FileSize,
        string StageName,
        DateTimeOffset UploadedAtUtc,
        string UploadedBy);
}
