using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Remarks;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public enum BriefingDeskCommandOutcome
{
    Succeeded,
    Invalid,
    Forbidden,
    NotFound
}

public sealed record BriefingDeskCommandResult(BriefingDeskCommandOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == BriefingDeskCommandOutcome.Succeeded;

    public static BriefingDeskCommandResult Success(string message) => new(BriefingDeskCommandOutcome.Succeeded, message);

    public static BriefingDeskCommandResult Invalid(string message) => new(BriefingDeskCommandOutcome.Invalid, message);

    public static BriefingDeskCommandResult Forbidden() => new(BriefingDeskCommandOutcome.Forbidden, "This item is not assigned to you.");

    public static BriefingDeskCommandResult NotFound(string what) => new(BriefingDeskCommandOutcome.NotFound, $"{what} was not found.");
}

// SECTION: Briefing desk commands
// The desk posts through the same services as the project, idea and task pages, so every
// business rule, audit record and notification stays in one place. The desk adds one rule
// of its own: it only acts on work assigned to the signed-in Project Officer.
public sealed class BriefingDeskCommandService
{
    public const int MaxBodyLength = 4000;

    // A closure note or a blocker is also written to the task's audit log, which holds 2000
    // characters (ActionTaskService refuses longer), so the desk asks for no more than that.
    public const int MaxTaskNoteLength = 2000;

    private readonly ApplicationDbContext _db;
    private readonly IRemarkService _remarks;
    private readonly ProjectIdeaCommandService _ideaCommands;
    private readonly ProjectIdeaPermissionService _ideaPermissions;
    private readonly IActionTaskService _tasks;
    private readonly IActionTaskCollaborationService _taskCollaboration;
    private readonly IActionTrackerClock _clock;

    public BriefingDeskCommandService(
        ApplicationDbContext db,
        IRemarkService remarks,
        ProjectIdeaCommandService ideaCommands,
        ProjectIdeaPermissionService ideaPermissions,
        IActionTaskService tasks,
        IActionTaskCollaborationService taskCollaboration,
        IActionTrackerClock clock)
    {
        _db = db;
        _remarks = remarks;
        _ideaCommands = ideaCommands;
        _ideaPermissions = ideaPermissions;
        _tasks = tasks;
        _taskCollaboration = taskCollaboration;
        _clock = clock;
    }

    // SECTION: Project update (an Internal remark by the assigned PO)
    public async Task<BriefingDeskCommandResult> PostProjectUpdateAsync(
        ClaimsPrincipal user,
        string userId,
        int projectId,
        string? body,
        CancellationToken ct)
    {
        if (!TryNormalizeBody(body, out var text, out var error)) return error!;
        if (!user.IsInRole(RoleNames.ProjectOfficer)) return BriefingDeskCommandResult.Forbidden();

        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId && !p.IsDeleted)
            .Select(p => new { p.Id, p.LeadPoUserId })
            .FirstOrDefaultAsync(ct);
        if (project is null) return BriefingDeskCommandResult.NotFound("The project");
        if (!string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase)) return BriefingDeskCommandResult.Forbidden();

        var actor = new RemarkActorContext(userId, RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        try
        {
            await _remarks.CreateRemarkAsync(
                new CreateRemarkRequest(
                    ProjectId: projectId,
                    Actor: actor,
                    Type: RemarkType.Internal,
                    Scope: RemarkScope.General,
                    Body: text,
                    EventDate: DateOnly.FromDateTime(_clock.IstToday),
                    StageRef: null,
                    StageNameSnapshot: null,
                    Meta: null),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            return BriefingDeskCommandResult.Invalid(ex.Message);
        }

        return BriefingDeskCommandResult.Success("Update posted.");
    }

    // SECTION: Idea note by the assigned PO
    public async Task<BriefingDeskCommandResult> PostIdeaNoteAsync(
        ClaimsPrincipal user,
        string userId,
        int ideaId,
        string? body,
        CancellationToken ct)
    {
        if (!TryNormalizeBody(body, out var text, out var error)) return error!;

        var idea = await _db.ProjectIdeas.FirstOrDefaultAsync(i => i.Id == ideaId && !i.IsDeleted, ct);
        if (idea is null) return BriefingDeskCommandResult.NotFound("The idea");
        if (!string.Equals(idea.AssignedProjectOfficerUserId, userId, StringComparison.Ordinal)
            || !_ideaPermissions.CanAddNote(user, idea))
        {
            return BriefingDeskCommandResult.Forbidden();
        }

        var title = $"Progress update · {_clock.IstToday.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
        try
        {
            await _ideaCommands.AddNoteAsync(idea, title, text, pinned: false, userId);
        }
        catch (InvalidOperationException ex)
        {
            return BriefingDeskCommandResult.Invalid(ex.Message);
        }

        return BriefingDeskCommandResult.Success("Note posted.");
    }

    // SECTION: Task progress, submit for closure and flag blocked
    public async Task<BriefingDeskCommandResult> PostTaskProgressAsync(
        ClaimsPrincipal user,
        string userId,
        int taskId,
        string? body,
        CancellationToken ct)
    {
        if (!TryNormalizeBody(body, out var text, out var error)) return error!;
        var (role, denied) = await AuthorizeTaskAsync(user, userId, taskId, ct);
        if (denied is not null) return denied;

        try
        {
            await _taskCollaboration.AddUpdateAsync(taskId, text, ActionTaskUpdateTypes.Comment, userId, role, Array.Empty<IFormFile>(), ct);
        }
        catch (InvalidOperationException ex)
        {
            return BriefingDeskCommandResult.Invalid(ex.Message);
        }

        return BriefingDeskCommandResult.Success("Progress posted.");
    }

    public async Task<BriefingDeskCommandResult> SubmitTaskAsync(
        ClaimsPrincipal user,
        string userId,
        int taskId,
        string? rowVersion,
        string? remarks,
        CancellationToken ct)
    {
        if (!TryNormalizeBody(remarks, out var text, out var error, "Add a closure note.", MaxTaskNoteLength)) return error!;
        var (role, denied) = await AuthorizeTaskAsync(user, userId, taskId, ct);
        if (denied is not null) return denied;

        try
        {
            await _tasks.SubmitTaskAsync(taskId, DecodeRowVersion(rowVersion), userId, role, text, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BriefingDeskCommandResult.Invalid(ex.Message);
        }

        return BriefingDeskCommandResult.Success("Task submitted for closure.");
    }

    public async Task<BriefingDeskCommandResult> FlagTaskBlockedAsync(
        ClaimsPrincipal user,
        string userId,
        int taskId,
        string? rowVersion,
        string? reason,
        CancellationToken ct)
    {
        if (!TryNormalizeBody(reason, out var text, out var error, "Say what is blocking the task.", MaxTaskNoteLength)) return error!;
        var (role, denied) = await AuthorizeTaskAsync(user, userId, taskId, ct);
        if (denied is not null) return denied;

        // ActionTaskService treats Blocked to Blocked as nothing to do and saves no reason, so a
        // stale page would report success and lose the text. Say so instead.
        var status = await _db.ActionTasks
            .AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(ct);
        if (string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase))
        {
            return BriefingDeskCommandResult.Invalid("This task is already flagged blocked. Post the new reason as progress instead.");
        }

        try
        {
            await _tasks.UpdateStatusAsync(taskId, DecodeRowVersion(rowVersion), ActionTaskStatuses.Blocked, userId, role, text, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BriefingDeskCommandResult.Invalid(ex.Message);
        }

        return BriefingDeskCommandResult.Success("Task flagged blocked.");
    }

    // SECTION: Shared guards
    private async Task<(string Role, BriefingDeskCommandResult? Denied)> AuthorizeTaskAsync(
        ClaimsPrincipal user,
        string userId,
        int taskId,
        CancellationToken ct)
    {
        var assignee = await _db.ActionTasks
            .AsNoTracking()
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => t.AssignedToUserId)
            .FirstOrDefaultAsync(ct);
        if (assignee is null) return (string.Empty, BriefingDeskCommandResult.NotFound("The task"));
        if (!string.Equals(assignee, userId, StringComparison.Ordinal)) return (string.Empty, BriefingDeskCommandResult.Forbidden());

        var role = ActionTaskRoleResolver.Resolve(user);
        return string.IsNullOrWhiteSpace(role)
            ? (string.Empty, BriefingDeskCommandResult.Forbidden())
            : (role, null);
    }

    private static bool TryNormalizeBody(
        string? body,
        out string text,
        out BriefingDeskCommandResult? error,
        string emptyMessage = "Write something before posting.",
        int maxLength = MaxBodyLength)
    {
        text = body?.Trim() ?? string.Empty;
        error = null;
        if (text.Length == 0)
        {
            error = BriefingDeskCommandResult.Invalid(emptyMessage);
            return false;
        }

        if (text.Length > maxLength)
        {
            error = BriefingDeskCommandResult.Invalid($"Keep it to {maxLength} characters or fewer.");
            return false;
        }

        return true;
    }

    private static byte[] DecodeRowVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            throw new InvalidOperationException("Task version is missing. Reload the page and try again.");
        }

        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Task version is invalid. Reload the page and try again.");
        }
    }
}
