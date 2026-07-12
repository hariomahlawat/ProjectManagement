using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Remarks;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.ConferenceRemarks;

/// <summary>
/// Writes conference directions to the native remarks stream of the selected source item.
/// The service derives actor identity, authority and state snapshots on the server and rejects
/// item identifiers that are not part of the selected officer's current active workload.
/// </summary>
public sealed class ConferenceRemarkCommandService : IConferenceRemarkCommandService
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRemarkService _projectRemarks;
    private readonly ProjectIdeaCommandService _ideaCommands;
    private readonly IActionTaskCollaborationService _taskCollaboration;
    private readonly IClock _clock;

    public ConferenceRemarkCommandService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IRemarkService projectRemarks,
        ProjectIdeaCommandService ideaCommands,
        IActionTaskCollaborationService taskCollaboration,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _projectRemarks = projectRemarks ?? throw new ArgumentNullException(nameof(projectRemarks));
        _ideaCommands = ideaCommands ?? throw new ArgumentNullException(nameof(ideaCommands));
        _taskCollaboration = taskCollaboration ?? throw new ArgumentNullException(nameof(taskCollaboration));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<AddConferenceRemarkResult> AddAsync(
        string requestingUserId,
        AddConferenceRemarkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Conference direction cannot be empty.");
        }

        if (body.Length > 4000)
        {
            throw new InvalidOperationException("Conference direction cannot exceed 4,000 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.OfficerUserId)
            || request.ItemId <= 0)
        {
            throw new InvalidOperationException("The selected workload item is invalid.");
        }

        var actor = await _users.Users
            .SingleOrDefaultAsync(user => user.Id == requestingUserId, cancellationToken);
        if (actor is null || actor.IsDisabled || actor.PendingDeletion)
        {
            throw new UnauthorizedAccessException("The current user account is unavailable.");
        }

        var assignedRoles = await _users.GetRolesAsync(actor);
        var commandRole = ResolveCommandRole(assignedRoles);
        if (commandRole is null)
        {
            throw new UnauthorizedAccessException("Only Comdt or HoD may add conference remarks.");
        }

        return request.Kind switch
        {
            ConferenceItemKind.Project => await AddProjectRemarkAsync(
                actor,
                assignedRoles,
                commandRole,
                request,
                body,
                cancellationToken),
            ConferenceItemKind.ProjectIdea => await AddIdeaRemarkAsync(
                actor,
                commandRole,
                request,
                body,
                cancellationToken),
            ConferenceItemKind.ActionTask => await AddTaskRemarkAsync(
                actor,
                commandRole,
                request,
                body,
                cancellationToken),
            _ => throw new InvalidOperationException("The selected workload item type is not supported.")
        };
    }

    private async Task<AddConferenceRemarkResult> AddProjectRemarkAsync(
        ApplicationUser actor,
        IEnumerable<string> assignedRoles,
        string commandRole,
        AddConferenceRemarkRequest request,
        string body,
        CancellationToken cancellationToken)
    {
        var belongsToOfficer = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project =>
                project.Id == request.ItemId
                && !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active
                && project.LeadPoUserId == request.OfficerUserId,
                cancellationToken);
        if (!belongsToOfficer)
        {
            throw new InvalidOperationException("This project is not part of the selected officer's current workload.");
        }

        var remarkRoles = assignedRoles
            .Select(role => RemarkActorRoleExtensions.TryParse(role, out var parsed)
                ? parsed
                : RemarkActorRole.Unknown)
            .Where(role => role != RemarkActorRole.Unknown)
            .Distinct()
            .ToArray();
        var actorRole = string.Equals(commandRole, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)
            ? RemarkActorRole.Commandant
            : RemarkActorRole.HeadOfDepartment;

        var remark = await _projectRemarks.CreateRemarkAsync(
            new CreateRemarkRequest(
                request.ItemId,
                new RemarkActorContext(actor.Id, actorRole, remarkRoles),
                RemarkType.Conference,
                RemarkScope.General,
                body,
                DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow.UtcDateTime)),
                StageRef: null,
                StageNameSnapshot: null,
                Meta: "Officer conference review"),
            cancellationToken);

        return new AddConferenceRemarkResult(
            new ConferenceDirectionVm
            {
                Id = remark.Id,
                Body = ToPlainText(remark.Body),
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(remark.CreatedAtUtc),
                SnapshotLabel = "Stage when issued",
                SnapshotValue = BuildStageSnapshot(remark.StageRef, remark.StageNameSnapshot)
            },
            "No stage movement · no subsequent update",
            null);
    }

    private async Task<AddConferenceRemarkResult> AddIdeaRemarkAsync(
        ApplicationUser actor,
        string commandRole,
        AddConferenceRemarkRequest request,
        string body,
        CancellationToken cancellationToken)
    {
        var idea = await _db.ProjectIdeas
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == request.ItemId
                && !candidate.IsDeleted
                && candidate.Status != ProjectIdeaStatuses.Archived
                && candidate.AssignedProjectOfficerUserId == request.OfficerUserId,
                cancellationToken);
        if (idea is null)
        {
            throw new InvalidOperationException("This idea is not part of the selected officer's current workload.");
        }

        var comment = await _ideaCommands.AddConferenceCommentAsync(
            idea,
            body,
            actor.Id,
            commandRole,
            cancellationToken);

        return new AddConferenceRemarkResult(
            new ConferenceDirectionVm
            {
                Id = comment.Id,
                Body = comment.CommentText,
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(comment.CreatedAt),
                SnapshotLabel = "Status when issued",
                SnapshotValue = ProjectIdeaStatuses.ToDisplay(comment.StatusSnapshot ?? idea.Status)
            },
            "No status movement · no subsequent update",
            null);
    }

    private async Task<AddConferenceRemarkResult> AddTaskRemarkAsync(
        ApplicationUser actor,
        string commandRole,
        AddConferenceRemarkRequest request,
        string body,
        CancellationToken cancellationToken)
    {
        var task = await _db.ActionTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == request.ItemId
                && !candidate.IsDeleted
                && candidate.Status != ActionTaskStatuses.Closed
                && candidate.Status != ActionTaskStatuses.Backlog
                && candidate.AssignedToUserId == request.OfficerUserId,
                cancellationToken);
        if (task is null)
        {
            throw new InvalidOperationException("This task is not part of the selected officer's current workload.");
        }

        var update = await _taskCollaboration.AddUpdateAsync(
            task.Id,
            body,
            ActionTaskUpdateTypes.Conference,
            actor.Id,
            commandRole,
            Array.Empty<IFormFile>(),
            cancellationToken);

        return new AddConferenceRemarkResult(
            new ConferenceDirectionVm
            {
                Id = update.Id,
                Body = update.Body,
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(update.CreatedAtUtc),
                SnapshotLabel = "When issued",
                SnapshotValue = BuildTaskSnapshot(update.StatusSnapshot, update.DueDateSnapshot)
            },
            "No status movement · due date unchanged · no subsequent update",
            null);
    }

    private static string? ResolveCommandRole(IEnumerable<string> roles)
    {
        if (roles.Any(role => string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)))
        {
            return RoleNames.Comdt;
        }

        return roles.Any(role => string.Equals(role, RoleNames.HoD, StringComparison.OrdinalIgnoreCase))
            ? RoleNames.HoD
            : null;
    }

    private static string DisplayName(ApplicationUser user)
        => string.IsNullOrWhiteSpace(user.FullName)
            ? user.UserName ?? user.Id
            : user.FullName;

    private static string DisplayRole(string role)
        => string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)
            ? "Comdt"
            : "HoD";

    private static string BuildStageSnapshot(string? stageRef, string? stageName)
    {
        if (string.IsNullOrWhiteSpace(stageRef) && string.IsNullOrWhiteSpace(stageName))
        {
            return "Not recorded";
        }

        if (string.IsNullOrWhiteSpace(stageName)
            || string.Equals(stageRef, stageName, StringComparison.OrdinalIgnoreCase))
        {
            return stageRef ?? stageName ?? "Not recorded";
        }

        return $"{stageRef} · {stageName}";
    }

    private static string BuildTaskSnapshot(string? status, DateOnly? dueDate)
    {
        var state = string.IsNullOrWhiteSpace(status) ? "Status not recorded" : status;
        return dueDate.HasValue
            ? $"{state} · due {dueDate.Value:dd MMM yyyy}"
            : state;
    }

    private static string ToPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex.Replace(value, " ");
        return WhitespaceRegex.Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
