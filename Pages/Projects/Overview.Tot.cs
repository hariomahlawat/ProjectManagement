using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Services.Authorization;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects;

/// <summary>
/// The project page's ToT drawer handlers. The drawer's work is shared with the ToT register
/// (<see cref="ProjectTotDrawerService"/>); this page supplies its own access rule: HoD and Admin
/// for any project, a Project Officer for a project they lead.
/// </summary>
public partial class OverviewModel
{
    public async Task<IActionResult> OnGetTotDetailsAsync(int id, CancellationToken ct)
        => ToJson(await TotDrawer().GetDetailsAsync(id, TotDrawerAccess(), ct));

    public async Task<IActionResult> OnPostTotAsync(int id, [FromForm] ProjectTotDrawerInput? input, CancellationToken ct)
    {
        var response = await TotDrawer().SaveAsync(id, input, TotDrawerAccess(), ct);
        if (response.StatusCode == 200)
        {
            _logger.LogInformation(
                "Project ToT saved from overview. ProjectId={ProjectId}, UserId={UserId}, Status={Status}",
                id,
                _users.GetUserId(User),
                input?.Status);
        }

        return ToJson(response);
    }

    public async Task<IActionResult> OnPostTotDecideAsync(
        int id,
        [FromForm] bool approve,
        [FromForm] string? rowVersion,
        [FromForm] string? reason,
        CancellationToken ct)
    {
        var response = await TotDrawer().DecideAsync(id, approve, rowVersion, reason, TotDrawerAccess(), ct);
        if (response.StatusCode == 200)
        {
            _logger.LogInformation(
                "Project ToT request {Decision} from overview. ProjectId={ProjectId}, UserId={UserId}",
                approve ? "approved" : "rejected",
                id,
                _users.GetUserId(User));
        }

        return ToJson(response);
    }

    public async Task<IActionResult> OnPostTotRemarkAsync(
        int id,
        [FromForm] int projectId,
        [FromForm] string? body,
        CancellationToken ct)
    {
        // Through the remark service, like the project page's Remarks panel: creation audit and
        // notifications included. Resolved per request so the page's constructor stays as it is.
        var remarks = HttpContext.RequestServices.GetRequiredService<IRemarkService>();
        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(claim => claim.Value);
        return ToJson(await TotDrawer().AddRemarkAsync(id, projectId, body, roles, remarks, TotDrawerAccess(), ct));
    }

    /// <summary>The shared drawer, as this page shows it: one project, with a link to the register.</summary>
    public ProjectTotDrawerViewModel TotDrawerView => new()
    {
        Endpoint = Url.Page("/Projects/Overview", new { id = Project.Id }) ?? $"/Projects/Overview/{Project.Id}",
        ProjectId = Project.Id,
        CanManage = CanManageTot,
        SaveNeedsApproval = TotSaveNeedsApproval,
        CanDecide = ApprovalAuthorization.CanApproveProjectChanges(User),
        IsApplicable = TotSummary.IsApplicable,
        Pending = TotSummary.PendingRequest is { State: ProjectTotRequestDecisionState.Pending },
        StatusLabel = TotSummary.StatusLabel,
        Summary = TotSummary.Summary,
        LatestRemarkBody = TotSummary.LatestRemark?.Body,
        LatestRemarkMeta = TotSummary.LatestRemark is { } remark
            ? $"{remark.TypeLabel} · {remark.AuthorDisplayName ?? "Unknown"}"
            : null,
        RegisterUrl = Url.Page("/Tot/Index", new { area = "ProjectOfficeReports", selectedProjectId = Project.Id })
    };

    private ProjectTotDrawerService TotDrawer() => new(_db, _clock);

    private ProjectTotDrawerAccess TotDrawerAccess()
        => new(
            _users.GetUserId(User),
            CanManageTotFromOverviewAsync,
            ApprovalAuthorization.CanApproveProjectChanges(User),
            User.IsInRole("Admin"),
            User.IsInRole("HoD"));

    private static JsonResult ToJson(ProjectTotDrawerResponse response)
        => new(response.Body) { StatusCode = response.StatusCode };

    private async Task<bool> CanManageTotFromOverviewAsync(Project project, CancellationToken ct)
    {
        if (project.IsArchived)
        {
            return false;
        }

        var currentUserId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return false;
        }

        if (User.IsInRole("Admin") || User.IsInRole("HoD"))
        {
            return await _db.Projects
                .AsNoTracking()
                .AnyAsync(item => item.Id == project.Id && !item.IsDeleted, ct);
        }

        return User.IsInRole("Project Officer") &&
               string.Equals(project.LeadPoUserId, currentUserId, StringComparison.OrdinalIgnoreCase);
    }
}
