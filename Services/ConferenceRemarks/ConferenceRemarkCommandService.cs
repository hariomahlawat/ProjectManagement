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
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.ConferenceRemarks;

/// <summary>
/// Writes conference directions to the native remarks stream of the selected source item.
/// The service derives actor identity, authority and state snapshots on the server and uses the
/// shared conference scope so read and write behaviour remain aligned for active and carryover work.
/// </summary>
public sealed class ConferenceRemarkCommandService : IConferenceRemarkCommandService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRemarkService _projectRemarks;
    private readonly IProjectIdeaCommandService _ideaCommands;
    private readonly IActionTaskCollaborationService _taskCollaboration;
    private readonly IClock _clock;
    private readonly IConferenceProjectScopeService? _projectScope;
    private readonly IItProjectRemarkService? _itProjectRemarks;

    // Retained for isolated tests and legacy composition roots. Application DI resolves
    // the complete constructor below.
    public ConferenceRemarkCommandService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IRemarkService projectRemarks,
        IProjectIdeaCommandService ideaCommands,
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

    public ConferenceRemarkCommandService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IRemarkService projectRemarks,
        IProjectIdeaCommandService ideaCommands,
        IActionTaskCollaborationService taskCollaboration,
        IClock clock,
        IConferenceProjectScopeService projectScope)
        : this(db, users, projectRemarks, ideaCommands, taskCollaboration, clock)
    {
        _projectScope = projectScope ?? throw new ArgumentNullException(nameof(projectScope));
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ConferenceRemarkCommandService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IRemarkService projectRemarks,
        IProjectIdeaCommandService ideaCommands,
        IActionTaskCollaborationService taskCollaboration,
        IClock clock,
        IConferenceProjectScopeService projectScope,
        IItProjectRemarkService itProjectRemarks)
        : this(db, users, projectRemarks, ideaCommands, taskCollaboration, clock, projectScope)
    {
        _itProjectRemarks = itProjectRemarks ?? throw new ArgumentNullException(nameof(itProjectRemarks));
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
            ConferenceItemKind.ItProject => await AddItProjectRemarkAsync(
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
        var belongsToOfficer = _projectScope is not null
            ? await _projectScope.IsProjectInScopeAsync(
                request.OfficerUserId,
                request.ItemId,
                cancellationToken)
            : await _db.Projects
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
            throw new InvalidOperationException(
                "This project is not part of the selected officer's conference review.");
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
                Meta: RemarkAuditMetadata.ForOfficerConferenceReview(request.OfficerUserId)),
            cancellationToken);

        return new AddConferenceRemarkResult(
            new ConferenceDirectionVm
            {
                Id = remark.Id,
                Body = ConferenceDirectionTextFormatter.ToDisplayText(remark.Body),
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(remark.CreatedAtUtc),
                SnapshotLabel = BuildProjectSnapshotLabel(remark.StageRef, remark.StageNameSnapshot),
                SnapshotValue = BuildStageSnapshot(remark.StageRef, remark.StageNameSnapshot)
            },
            new[]
            {
                new ConferenceProgressEntryVm
                {
                    Label = "Project Officer",
                    EmptyText = "No remark by the Project Officer after the direction."
                }
            },
            null,
            string.Empty,
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
                Body = ConferenceDirectionTextFormatter.ToDisplayText(comment.CommentText),
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(comment.CreatedAt),
                SnapshotLabel = "Status when issued",
                SnapshotValue = ProjectIdeaStatuses.ToDisplay(comment.StatusSnapshot ?? idea.Status)
            },
            Array.Empty<ConferenceProgressEntryVm>(),
            "No comment or note after the direction.",
            string.Empty,
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
                Body = ConferenceDirectionTextFormatter.ToDisplayText(update.Body),
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(update.CreatedAtUtc),
                SnapshotLabel = "When issued",
                SnapshotValue = BuildTaskSnapshot(update.StatusSnapshot, update.DueDateSnapshot)
            },
            new[]
            {
                new ConferenceProgressEntryVm
                {
                    Label = "Task Assignee",
                    EmptyText = "No update by the task assignee after the direction."
                }
            },
            null,
            string.Empty,
            null);
    }

    /// <summary>
    /// A conference direction on an IT project: stored as the IT project's own conference remark.
    /// IT projects sit under the ITO on the review, so the selected officer must hold that role.
    /// </summary>
    private async Task<AddConferenceRemarkResult> AddItProjectRemarkAsync(
        ApplicationUser actor,
        string commandRole,
        AddConferenceRemarkRequest request,
        string body,
        CancellationToken cancellationToken)
    {
        if (_itProjectRemarks is null)
        {
            throw new InvalidOperationException("The selected workload item type is not supported.");
        }

        var officer = await _users.Users.SingleOrDefaultAsync(user => user.Id == request.OfficerUserId, cancellationToken);
        if (officer is null || !await _users.IsInRoleAsync(officer, RoleNames.Ito))
        {
            throw new InvalidOperationException("IT projects are reviewed under the ITO.");
        }

        var project = await _db.ItProjects
            .AsNoTracking()
            .Include(candidate => candidate.Stages)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.ItemId && candidate.Module == ItProjectModules.It && !candidate.IsDeleted && candidate.DroppedOn == null, cancellationToken);
        if (project is null || ItProjectStatus.Resolve(project).Kind is not (ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress))
        {
            throw new InvalidOperationException("This IT project is not part of the selected officer's current workload.");
        }

        var (result, remarkId) = await _itProjectRemarks.AddAsync(
            project.Id,
            new ItProjectRemarkInput(body, ItProjectYearTypes.TodayIst(_clock.UtcNow), ItProjectRemarkType.Conference),
            new ItProjectRemarkActor(actor.Id, actor.UserName, commandRole, IsAdmin: false),
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors));
        }

        var remark = await _db.ItProjectRemarks.AsNoTracking().SingleAsync(candidate => candidate.Id == remarkId, cancellationToken);
        return new AddConferenceRemarkResult(
            new ConferenceDirectionVm
            {
                Id = remark.Id,
                Body = ConferenceDirectionTextFormatter.ToDisplayText(remark.Body),
                AuthorName = DisplayName(actor),
                AuthorRole = DisplayRole(commandRole),
                CreatedAtUtc = AsUtc(remark.CreatedAtUtc),
                SnapshotLabel = "Stage when issued",
                SnapshotValue = remark.StageCode is { } code && ItProjectStages.IsKnown(code) ? ItProjectStages.LabelOf(code) : "Not recorded"
            },
            Array.Empty<ConferenceProgressEntryVm>(),
            "Progress update awaited. The ITO has not written a remark since the direction was issued.",
            string.Empty,
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

    private static string BuildProjectSnapshotLabel(string? stageRef, string? stageName)
        => string.IsNullOrWhiteSpace(stageRef)
           && string.Equals(stageName, "Completed", StringComparison.OrdinalIgnoreCase)
            ? "Status when issued"
            : "Stage when issued";

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

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
