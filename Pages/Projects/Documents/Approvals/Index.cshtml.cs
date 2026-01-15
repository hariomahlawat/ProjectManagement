using System;
using System.Collections.Generic;
using System.Globalization;
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
using ProjectManagement.Services.Authorization;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.Projects.Documents.Approvals;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;

    public IndexModel(ApplicationDbContext db, IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public ProjectSummaryViewModel Project { get; private set; } = default!;

    public IReadOnlyList<PendingRequestViewModel> Requests { get; private set; } = Array.Empty<PendingRequestViewModel>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanModerate())
        {
            return Forbid();
        }

        Project = new ProjectSummaryViewModel(project.Id, project.Name);
        ViewData["Title"] = string.Format(CultureInfo.InvariantCulture, "Document approvals – {0}", project.Name);

        Requests = await LoadPendingRequestsAsync(id, cancellationToken);

        return Page();
    }

    private bool UserCanModerate()
        => ApprovalAuthorization.CanApproveProjectChanges(_userContext.User);

    private async Task<IReadOnlyList<PendingRequestViewModel>> LoadPendingRequestsAsync(int projectId, CancellationToken cancellationToken)
    {
        var pending = await _db.ProjectDocumentRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Status == ProjectDocumentRequestStatus.Submitted)
            .OrderBy(r => r.RequestedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.RequestType,
                r.FileSize,
                r.OriginalFileName,
                r.RequestedAtUtc,
                StageCode = r.Stage != null ? r.Stage.StageCode : null,
                RequestedByName = r.RequestedByUser != null
                    ? (string.IsNullOrWhiteSpace(r.RequestedByUser.FullName)
                        ? (r.RequestedByUser.UserName ?? r.RequestedByUser.Email ?? "Unknown")
                        : r.RequestedByUser.FullName)
                    : "Unknown"
            })
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return Array.Empty<PendingRequestViewModel>();
        }

        var ist = TimeZoneHelper.GetIst();
        return pending
            .Select(r => new PendingRequestViewModel(
                r.Id,
                r.Title,
                DescribeAction(r.RequestType),
                string.IsNullOrWhiteSpace(r.StageCode) ? "General" : StageCodes.DisplayNameOf(r.StageCode),
                r.OriginalFileName ?? "—",
                r.FileSize,
                r.RequestedByName,
                TimeZoneInfo.ConvertTime(r.RequestedAtUtc, ist)))
            .ToList();
    }

    private static string DescribeAction(ProjectDocumentRequestType type) => type switch
    {
        ProjectDocumentRequestType.Upload => "Publish new",
        ProjectDocumentRequestType.Replace => "Overwrite",
        ProjectDocumentRequestType.Delete => "Remove",
        _ => type.ToString()
    };

    public sealed record ProjectSummaryViewModel(int Id, string Name);

    public sealed record PendingRequestViewModel(
        int RequestId,
        string Nomenclature,
        string Action,
        string Stage,
        string OriginalFileName,
        long? FileSize,
        string RequestedBy,
        DateTimeOffset RequestedAtLocal);
}
