using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.ConferenceRemarks;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Builds the compact command conference view from the same project, idea and action-task
/// records used by the operational modules. All source data is loaded in bounded batch
/// queries; conference remarks remain native records in their respective modules.
/// </summary>
public sealed class OfficerConferenceReadService : IOfficerConferenceReadService
{
    private readonly ApplicationDbContext _db;
    private readonly IOfficerWorkloadReadService _workload;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
    private readonly IClock _clock;

    public OfficerConferenceReadService(
        ApplicationDbContext db,
        IOfficerWorkloadReadService workload,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _workflowStageMetadataProvider = workflowStageMetadataProvider
            ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<OfficerConferenceVm?> GetAsync(
        string requestingUserId,
        string officerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestingUserId)
            || string.IsNullOrWhiteSpace(officerUserId))
        {
            return null;
        }

        var orderedOfficers = await _workload.GetAllAsync(requestingUserId, cancellationToken);
        var selectedIndex = orderedOfficers
            .Select((officer, index) => new { officer, index })
            .FirstOrDefault(entry => string.Equals(
                entry.officer.UserId,
                officerUserId,
                StringComparison.Ordinal));

        if (selectedIndex is null)
        {
            return null;
        }

        var selected = selectedIndex.officer;
        var projectIds = selected.Projects.Select(item => item.ProjectId).Distinct().ToArray();
        var ideaIds = selected.Ideas.Select(item => item.IdeaId).Distinct().ToArray();
        var taskIds = selected.OtherTasks.Select(item => item.TaskId).Distinct().ToArray();

        // A scoped DbContext does not support concurrent operations. Keep the query set
        // bounded and batched, but execute it sequentially to preserve EF Core safety.
        var projectRows = await LoadProjectsAsync(projectIds, cancellationToken);
        var ideaRows = await LoadIdeasAsync(ideaIds, cancellationToken);
        var taskRows = await LoadTasksAsync(taskIds, cancellationToken);

        var latestProjectDirections = (await LoadLatestProjectDirectionsAsync(projectIds, cancellationToken))
            .ToDictionary(direction => direction.ProjectId);
        var latestIdeaDirections = (await LoadLatestIdeaDirectionsAsync(ideaIds, cancellationToken))
            .ToDictionary(direction => direction.ProjectIdeaId);
        var latestTaskDirections = (await LoadLatestTaskDirectionsAsync(taskIds, cancellationToken))
            .ToDictionary(direction => direction.TaskId);

        // Only operational activity after the oldest latest direction can contribute to the
        // progress summaries. This prevents the conference view from loading complete remark
        // histories for long-running projects, ideas and tasks.
        // A multi-role user can submit a remark under HoD/Comdt even while being the
        // assigned Project Officer. Assignment therefore determines the PO response;
        // the persisted author-role snapshot is retained only for audit/display purposes.
        var assignedProjectOfficerUserIds = projectRows
            .Select(project => string.IsNullOrWhiteSpace(project.LeadPoUserId)
                ? selected.UserId
                : project.LeadPoUserId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlySet<string> mcoUserIds = latestProjectDirections.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : await LoadUserIdsInRoleAsync(RoleNames.Mco, cancellationToken);
        var projectRemarks = await LoadProjectProgressRemarksAsync(
            latestProjectDirections,
            assignedProjectOfficerUserIds,
            mcoUserIds,
            cancellationToken);
        var ideaComments = await LoadIdeaProgressCommentsAsync(latestIdeaDirections, cancellationToken);
        var ideaNotes = await LoadIdeaProgressNotesAsync(latestIdeaDirections, cancellationToken);
        var assignedTaskUserIds = taskRows
            .Select(task => string.IsNullOrWhiteSpace(task.AssignedToUserId)
                ? selected.UserId
                : task.AssignedToUserId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var taskUpdates = await LoadTaskProgressUpdatesAsync(
            latestTaskDirections,
            assignedTaskUserIds,
            cancellationToken);

        var authorIds = latestProjectDirections.Values.Select(item => item.AuthorUserId)
            .Concat(latestIdeaDirections.Values.Select(item => item.CreatedByUserId))
            .Concat(latestTaskDirections.Values.Select(item => item.CreatedByUserId))
            .Concat(projectRemarks.Select(item => item.AuthorUserId))
            .Concat(ideaComments.Select(item => item.CreatedByUserId))
            .Concat(ideaNotes.Select(item => item.CreatedByUserId))
            .Concat(taskUpdates.Select(item => item.CreatedByUserId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, string> authorNames;
        if (authorIds.Length == 0)
        {
            authorNames = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var authorRows = await _db.Users
                .AsNoTracking()
                .Where(user => authorIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Name = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.UserName ?? user.Id
                        : user.FullName
                })
                .ToListAsync(cancellationToken);

            authorNames = authorRows.ToDictionary(
                item => item.Id,
                item => item.Name,
                StringComparer.Ordinal);
        }

        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow.UtcDateTime));

        var projectItems = BuildProjectItems(
            selected,
            projectRows,
            projectRemarks,
            latestProjectDirections,
            authorNames,
            mcoUserIds,
            today);
        var ideaItems = BuildIdeaItems(
            selected,
            ideaRows,
            ideaComments,
            ideaNotes,
            latestIdeaDirections,
            authorNames);
        var taskItems = BuildTaskItems(
            selected,
            taskRows,
            taskUpdates,
            latestTaskDirections,
            authorNames,
            today);

        var officerOptions = orderedOfficers
            .Select(officer => new OfficerConferenceOfficerOptionVm(
                officer.UserId,
                DisplayOfficerName(officer),
                string.Equals(officer.UserId, selected.UserId, StringComparison.Ordinal)))
            .ToList();

        return new OfficerConferenceVm
        {
            OfficerUserId = selected.UserId,
            OfficerName = selected.OfficerName,
            OfficerRank = selected.Rank,
            OfficerInitial = InitialOf(selected.OfficerName),
            ProjectCount = projectItems.Count,
            IdeaCount = ideaItems.Count,
            OtherTaskCount = taskItems.Count,
            PreviousOfficerUserId = selectedIndex.index > 0
                ? orderedOfficers[selectedIndex.index - 1].UserId
                : null,
            NextOfficerUserId = selectedIndex.index + 1 < orderedOfficers.Count
                ? orderedOfficers[selectedIndex.index + 1].UserId
                : null,
            OfficerOptions = officerOptions,
            Sections = new[]
            {
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.Project,
                    Title = "Projects",
                    IconClass = "bi-kanban",
                    Items = projectItems
                },
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.ProjectIdea,
                    Title = "Ideas",
                    IconClass = "bi-lightbulb",
                    Items = ideaItems
                },
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.ActionTask,
                    Title = "Other tasks",
                    IconClass = "bi-list-check",
                    Items = taskItems
                }
            }
        };
    }

    private async Task<List<ProjectRow>> LoadProjectsAsync(
        int[] projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return new List<ProjectRow>();
        }

        return await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new ProjectRow(
                project.Id,
                project.Name,
                project.LeadPoUserId,
                project.WorkflowVersion,
                project.ProjectStages
                    .Select(stage => new ProjectStageRow(
                        stage.StageCode,
                        stage.Status,
                        stage.SortOrder,
                        stage.ActualStart,
                        stage.CompletedOn,
                        stage.PlannedDue))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaRow>> LoadIdeasAsync(
        int[] ideaIds,
        CancellationToken cancellationToken)
    {
        if (ideaIds.Length == 0)
        {
            return new List<IdeaRow>();
        }

        return await _db.ProjectIdeas
            .AsNoTracking()
            .Where(idea => ideaIds.Contains(idea.Id))
            .Select(idea => new IdeaRow(
                idea.Id,
                idea.Title,
                idea.Status,
                idea.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TaskRow>> LoadTasksAsync(
        int[] taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Length == 0)
        {
            return new List<TaskRow>();
        }

        return await _db.ActionTasks
            .AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .Select(task => new TaskRow(
                task.Id,
                task.Title,
                task.Status,
                task.DueDate,
                task.Priority,
                task.AssignedToUserId))
            .ToListAsync(cancellationToken);
    }

    private Task<List<Remark>> LoadLatestProjectDirectionsAsync(
        int[] projectIds,
        CancellationToken cancellationToken)
        => projectIds.Length == 0
            ? Task.FromResult(new List<Remark>())
            : _db.Remarks
                .AsNoTracking()
                .Where(direction => projectIds.Contains(direction.ProjectId)
                    && !direction.IsDeleted
                    && direction.Type == RemarkType.Conference)
                .Where(direction => !_db.Remarks.Any(candidate =>
                    candidate.ProjectId == direction.ProjectId
                    && !candidate.IsDeleted
                    && candidate.Type == RemarkType.Conference
                    && (candidate.CreatedAtUtc > direction.CreatedAtUtc
                        || (candidate.CreatedAtUtc == direction.CreatedAtUtc
                            && candidate.Id > direction.Id))))
                .ToListAsync(cancellationToken);

    private Task<List<ProjectIdeaComment>> LoadLatestIdeaDirectionsAsync(
        int[] ideaIds,
        CancellationToken cancellationToken)
        => ideaIds.Length == 0
            ? Task.FromResult(new List<ProjectIdeaComment>())
            : _db.ProjectIdeaComments
                .AsNoTracking()
                .Where(direction => ideaIds.Contains(direction.ProjectIdeaId)
                    && !direction.IsDeleted
                    && direction.CommentType == ProjectIdeaCommentTypes.Conference)
                .Where(direction => !_db.ProjectIdeaComments.Any(candidate =>
                    candidate.ProjectIdeaId == direction.ProjectIdeaId
                    && !candidate.IsDeleted
                    && candidate.CommentType == ProjectIdeaCommentTypes.Conference
                    && (candidate.CreatedAt > direction.CreatedAt
                        || (candidate.CreatedAt == direction.CreatedAt
                            && candidate.Id > direction.Id))))
                .ToListAsync(cancellationToken);

    private Task<List<ActionTaskUpdate>> LoadLatestTaskDirectionsAsync(
        int[] taskIds,
        CancellationToken cancellationToken)
        => taskIds.Length == 0
            ? Task.FromResult(new List<ActionTaskUpdate>())
            : _db.ActionTaskUpdates
                .AsNoTracking()
                .Where(direction => taskIds.Contains(direction.TaskId)
                    && !direction.IsDeleted
                    && direction.UpdateType == ActionTaskUpdateTypes.Conference)
                .Where(direction => !_db.ActionTaskUpdates.Any(candidate =>
                    candidate.TaskId == direction.TaskId
                    && !candidate.IsDeleted
                    && candidate.UpdateType == ActionTaskUpdateTypes.Conference
                    && (candidate.CreatedAtUtc > direction.CreatedAtUtc
                        || (candidate.CreatedAtUtc == direction.CreatedAtUtc
                            && candidate.Id > direction.Id))))
                .ToListAsync(cancellationToken);

    private Task<List<Remark>> LoadProjectProgressRemarksAsync(
        IReadOnlyDictionary<int, Remark> latestDirections,
        string[] assignedProjectOfficerUserIds,
        IReadOnlySet<string> mcoUserIds,
        CancellationToken cancellationToken)
    {
        if (latestDirections.Count == 0)
        {
            return Task.FromResult(new List<Remark>());
        }

        var projectIds = latestDirections.Keys.ToArray();
        var mcoUserIdArray = mcoUserIds.ToArray();
        var earliestDirection = latestDirections.Values.Min(direction => direction.CreatedAtUtc);
        return _db.Remarks
            .AsNoTracking()
            .Where(remark => projectIds.Contains(remark.ProjectId)
                && !remark.IsDeleted
                && remark.Type != RemarkType.Conference
                && (assignedProjectOfficerUserIds.Contains(remark.AuthorUserId)
                    || remark.AuthorRole == RemarkActorRole.Mco
                    || mcoUserIdArray.Contains(remark.AuthorUserId))
                && remark.CreatedAtUtc >= earliestDirection)
            .OrderBy(remark => remark.CreatedAtUtc)
            .ThenBy(remark => remark.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlySet<string>> LoadUserIdsInRoleAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var normalizedRoleName = roleName.ToUpperInvariant();
        var roleIds = await _db.Roles
            .AsNoTracking()
            .Where(role => role.Name == roleName || role.NormalizedName == normalizedRoleName)
            .Select(role => role.Id)
            .ToArrayAsync(cancellationToken);

        if (roleIds.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var userIds = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .Select(userRole => userRole.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return userIds.ToHashSet(StringComparer.Ordinal);
    }

    private Task<List<ProjectIdeaComment>> LoadIdeaProgressCommentsAsync(
        IReadOnlyDictionary<int, ProjectIdeaComment> latestDirections,
        CancellationToken cancellationToken)
    {
        if (latestDirections.Count == 0)
        {
            return Task.FromResult(new List<ProjectIdeaComment>());
        }

        var ideaIds = latestDirections.Keys.ToArray();
        var earliestDirection = latestDirections.Values.Min(direction => direction.CreatedAt);
        return _db.ProjectIdeaComments
            .AsNoTracking()
            .Where(comment => ideaIds.Contains(comment.ProjectIdeaId)
                && !comment.IsDeleted
                && comment.CommentType != ProjectIdeaCommentTypes.Conference
                && comment.CreatedAt >= earliestDirection)
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .ToListAsync(cancellationToken);
    }

    private Task<List<IdeaNoteRow>> LoadIdeaProgressNotesAsync(
        IReadOnlyDictionary<int, ProjectIdeaComment> latestDirections,
        CancellationToken cancellationToken)
    {
        if (latestDirections.Count == 0)
        {
            return Task.FromResult(new List<IdeaNoteRow>());
        }

        var ideaIds = latestDirections.Keys.ToArray();
        var earliestDirection = latestDirections.Values.Min(direction => direction.CreatedAt);
        return _db.ProjectIdeaNotes
            .AsNoTracking()
            .Where(note => ideaIds.Contains(note.ProjectIdeaId)
                && !note.IsDeleted
                && (note.CreatedAt >= earliestDirection || note.UpdatedAt >= earliestDirection))
            .Select(note => new IdeaNoteRow(
                note.Id,
                note.ProjectIdeaId,
                note.Title,
                note.Body,
                note.CreatedByUserId,
                note.CreatedAt,
                note.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    private Task<List<ActionTaskUpdate>> LoadTaskProgressUpdatesAsync(
        IReadOnlyDictionary<int, ActionTaskUpdate> latestDirections,
        string[] assignedTaskUserIds,
        CancellationToken cancellationToken)
    {
        if (latestDirections.Count == 0 || assignedTaskUserIds.Length == 0)
        {
            return Task.FromResult(new List<ActionTaskUpdate>());
        }

        var taskIds = latestDirections.Keys.ToArray();
        var earliestDirection = latestDirections.Values.Min(direction => direction.CreatedAtUtc);
        return _db.ActionTaskUpdates
            .AsNoTracking()
            .Where(update => taskIds.Contains(update.TaskId)
                && !update.IsDeleted
                && update.UpdateType != ActionTaskUpdateTypes.Conference
                && assignedTaskUserIds.Contains(update.CreatedByUserId)
                && update.CreatedAtUtc >= earliestDirection)
            .OrderBy(update => update.CreatedAtUtc)
            .ThenBy(update => update.Id)
            .ToListAsync(cancellationToken);
    }

    private IReadOnlyList<OfficerConferenceItemVm> BuildProjectItems(
        CommandOfficerWorkloadVm officer,
        IReadOnlyList<ProjectRow> rows,
        IReadOnlyList<Remark> remarks,
        IReadOnlyDictionary<int, Remark> latestDirections,
        IReadOnlyDictionary<string, string> authorNames,
        IReadOnlySet<string> mcoUserIds,
        DateOnly today)
    {
        var rowsById = rows.ToDictionary(row => row.Id);
        var remarksByProject = remarks
            .GroupBy(remark => remark.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<OfficerConferenceItemVm>();

        foreach (var workloadItem in officer.Projects)
        {
            if (!rowsById.TryGetValue(workloadItem.ProjectId, out var row))
            {
                continue;
            }

            var snapshots = row.Stages
                .Select(stage => new ProjectStageStatusSnapshot(
                    stage.StageCode,
                    stage.Status,
                    stage.SortOrder,
                    stage.ActualStart,
                    stage.CompletedOn))
                .ToList();
            var presentStage = PresentStageHelper.ComputePresentStageAndAge(
                snapshots,
                _workflowStageMetadataProvider,
                row.WorkflowVersion,
                today);
            var currentCode = presentStage.CurrentStageCode ?? workloadItem.StageCode;
            var currentName = presentStage.CurrentStageName ?? workloadItem.StageName;
            var currentPdc = row.Stages
                .FirstOrDefault(stage => string.Equals(
                    stage.StageCode,
                    currentCode,
                    StringComparison.OrdinalIgnoreCase))
                ?.PlannedDue;

            var contextParts = new List<string>();
            if (presentStage.DaysSinceStartOrLastCompletion.HasValue)
            {
                contextParts.Add($"{presentStage.DaysSinceStartOrLastCompletion.Value} days in stage");
            }

            string? attentionText = null;
            var requiresAttention = false;
            if (currentPdc.HasValue)
            {
                var delta = currentPdc.Value.DayNumber - today.DayNumber;
                if (delta < 0)
                {
                    attentionText = $"PDC overdue by {Math.Abs(delta)} day{(Math.Abs(delta) == 1 ? string.Empty : "s")}";
                    requiresAttention = true;
                }
                else
                {
                    contextParts.Add($"PDC {currentPdc.Value:dd MMM yyyy}");
                }
            }
            else
            {
                contextParts.Add("PDC not set");
            }

            latestDirections.TryGetValue(row.Id, out var direction);
            var itemRemarks = remarksByProject.TryGetValue(row.Id, out var foundRemarks)
                ? foundRemarks
                : new List<Remark>();
            var assignedProjectOfficerId = string.IsNullOrWhiteSpace(row.LeadPoUserId)
                ? officer.UserId
                : row.LeadPoUserId;

            Remark? latestProjectOfficerRemark = null;
            Remark? latestMcoRemark = null;
            if (direction is not null)
            {
                // The current project assignment is authoritative. Do not require the
                // remark's role snapshot to be ProjectOfficer because Identity role
                // precedence may have stored HoD or Comdt for the same user.
                latestProjectOfficerRemark = itemRemarks
                    .Where(remark => string.Equals(
                            remark.AuthorUserId,
                            assignedProjectOfficerId,
                            StringComparison.Ordinal)
                        && IsAfter(remark.CreatedAtUtc, remark.Id, direction.CreatedAtUtc, direction.Id))
                    .OrderByDescending(remark => remark.CreatedAtUtc)
                    .ThenByDescending(remark => remark.Id)
                    .FirstOrDefault();

                // Recognise MCO work from either the historical role snapshot or the
                // user's current MCO membership. Exclude the assigned PO so one remark
                // is never rendered twice when a user holds both appointments.
                latestMcoRemark = itemRemarks
                    .Where(remark => (remark.AuthorRole == RemarkActorRole.Mco
                            || mcoUserIds.Contains(remark.AuthorUserId))
                        && !string.Equals(
                            remark.AuthorUserId,
                            assignedProjectOfficerId,
                            StringComparison.Ordinal)
                        && IsAfter(remark.CreatedAtUtc, remark.Id, direction.CreatedAtUtc, direction.Id))
                    .OrderByDescending(remark => remark.CreatedAtUtc)
                    .ThenByDescending(remark => remark.Id)
                    .FirstOrDefault();
            }

            var progressEntries = new List<ConferenceProgressEntryVm>();
            if (direction is not null)
            {
                progressEntries.Add(latestProjectOfficerRemark is null
                    ? new ConferenceProgressEntryVm
                    {
                        Label = "Project Officer",
                        EmptyText = "No remark by the Project Officer after the direction."
                    }
                    : BuildRemarkProgressEntry(
                        "Project Officer",
                        latestProjectOfficerRemark,
                        authorNames));

                if (latestMcoRemark is not null)
                {
                    progressEntries.Add(BuildRemarkProgressEntry(
                        "MCO",
                        latestMcoRemark,
                        authorNames));
                }
            }

            result.Add(new OfficerConferenceItemVm
            {
                Kind = ConferenceItemKind.Project,
                ItemId = row.Id,
                Title = row.Name,
                OpenUrl = workloadItem.OpenUrl,
                CurrentStateCode = currentCode,
                CurrentStateName = currentName,
                CurrentContext = contextParts.Count == 0 ? null : string.Join(" · ", contextParts),
                AttentionText = attentionText,
                RequiresAttention = requiresAttention,
                LatestDirection = direction is null
                    ? null
                    : new ConferenceDirectionVm
                    {
                        Id = direction.Id,
                        Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
                        AuthorName = ResolveAuthor(authorNames, direction.AuthorUserId),
                        AuthorRole = DisplayRole(direction.AuthorRole),
                        CreatedAtUtc = AsUtc(direction.CreatedAtUtc),
                        SnapshotLabel = "Stage when issued",
                        SnapshotValue = BuildStageSnapshot(direction.StageRef, direction.StageNameSnapshot)
                    },
                ProgressEntries = progressEntries,
                EmptyProgressText = null,
                ProgressSummary = string.Empty,
                LatestProgressText = null
            });
        }

        return result;
    }

    private static IReadOnlyList<OfficerConferenceItemVm> BuildIdeaItems(
        CommandOfficerWorkloadVm officer,
        IReadOnlyList<IdeaRow> rows,
        IReadOnlyList<ProjectIdeaComment> comments,
        IReadOnlyList<IdeaNoteRow> notes,
        IReadOnlyDictionary<int, ProjectIdeaComment> latestDirections,
        IReadOnlyDictionary<string, string> authorNames)
    {
        var rowsById = rows.ToDictionary(row => row.Id);
        var commentsByIdea = comments
            .GroupBy(comment => comment.ProjectIdeaId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var notesByIdea = notes
            .GroupBy(note => note.ProjectIdeaId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<OfficerConferenceItemVm>();

        foreach (var workloadItem in officer.Ideas)
        {
            if (!rowsById.TryGetValue(workloadItem.IdeaId, out var row))
            {
                continue;
            }

            latestDirections.TryGetValue(row.Id, out var direction);
            var itemComments = commentsByIdea.TryGetValue(row.Id, out var foundComments)
                ? foundComments
                : new List<ProjectIdeaComment>();
            var itemNotes = notesByIdea.TryGetValue(row.Id, out var foundNotes)
                ? foundNotes
                : new List<IdeaNoteRow>();

            ProjectIdeaComment? latestComment = null;
            IdeaNoteRow? latestNote = null;
            if (direction is not null)
            {
                latestComment = itemComments
                    .Where(comment => !string.Equals(
                            comment.CommentType,
                            ProjectIdeaCommentTypes.Conference,
                            StringComparison.OrdinalIgnoreCase)
                        && IsAfter(comment.CreatedAt, comment.Id, direction.CreatedAt, direction.Id))
                    .OrderByDescending(comment => comment.CreatedAt)
                    .ThenByDescending(comment => comment.Id)
                    .FirstOrDefault();

                latestNote = itemNotes
                    .Where(note => NoteActivityAt(note) > direction.CreatedAt)
                    .OrderByDescending(NoteActivityAt)
                    .ThenByDescending(note => note.Id)
                    .FirstOrDefault();
            }

            var progressEntries = new List<ConferenceProgressEntryVm>();
            if (latestComment is not null)
            {
                progressEntries.Add(new ConferenceProgressEntryVm
                {
                    Label = "Latest comment",
                    Body = ConferenceDirectionTextFormatter.ToDisplayText(latestComment.CommentText),
                    AuthorName = ResolveAuthor(authorNames, latestComment.CreatedByUserId),
                    ActivityAtUtc = AsUtc(latestComment.CreatedAt)
                });
            }

            if (latestNote is not null)
            {
                progressEntries.Add(new ConferenceProgressEntryVm
                {
                    Label = "Latest note",
                    Title = latestNote.Title,
                    Body = ConferenceDirectionTextFormatter.ToDisplayText(latestNote.Body),
                    AuthorName = ResolveAuthor(authorNames, latestNote.CreatedByUserId),
                    ActivityAtUtc = AsUtc(NoteActivityAt(latestNote))
                });
            }

            result.Add(new OfficerConferenceItemVm
            {
                Kind = ConferenceItemKind.ProjectIdea,
                ItemId = row.Id,
                Title = row.Title,
                OpenUrl = workloadItem.OpenUrl,
                CurrentStateCode = row.Status,
                CurrentStateName = ProjectIdeaStatuses.ToDisplay(row.Status),
                CurrentContext = $"Updated {IstClock.ToIst(AsUtc(row.UpdatedAt)):dd MMM yyyy}",
                LatestDirection = direction is null
                    ? null
                    : new ConferenceDirectionVm
                    {
                        Id = direction.Id,
                        Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.CommentText),
                        AuthorName = ResolveAuthor(authorNames, direction.CreatedByUserId),
                        AuthorRole = DisplayRole(direction.CreatedByRole),
                        CreatedAtUtc = AsUtc(direction.CreatedAt),
                        SnapshotLabel = "Status when issued",
                        SnapshotValue = ProjectIdeaStatuses.ToDisplay(direction.StatusSnapshot ?? row.Status)
                    },
                ProgressEntries = progressEntries,
                EmptyProgressText = direction is not null && progressEntries.Count == 0
                    ? "No comment or note after the direction."
                    : null,
                ProgressSummary = string.Empty,
                LatestProgressText = null
            });
        }

        return result
            .OrderByDescending(item => rowsById[item.ItemId].UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<OfficerConferenceItemVm> BuildTaskItems(
        CommandOfficerWorkloadVm officer,
        IReadOnlyList<TaskRow> rows,
        IReadOnlyList<ActionTaskUpdate> updates,
        IReadOnlyDictionary<int, ActionTaskUpdate> latestDirections,
        IReadOnlyDictionary<string, string> authorNames,
        DateOnly today)
    {
        var rowsById = rows.ToDictionary(row => row.Id);
        var updatesByTask = updates
            .GroupBy(update => update.TaskId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<OfficerConferenceItemVm>();

        foreach (var workloadItem in officer.OtherTasks)
        {
            if (!rowsById.TryGetValue(workloadItem.TaskId, out var row))
            {
                continue;
            }

            var dueDate = DateOnly.FromDateTime(row.DueDate);
            var overdueDays = today.DayNumber - dueDate.DayNumber;
            var requiresAttention = overdueDays > 0
                || string.Equals(row.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);
            var attentionText = string.Equals(row.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)
                ? "Blocked"
                : overdueDays > 0
                    ? $"Overdue by {overdueDays} day{(overdueDays == 1 ? string.Empty : "s")}"
                    : null;

            latestDirections.TryGetValue(row.Id, out var direction);
            var itemUpdates = updatesByTask.TryGetValue(row.Id, out var foundUpdates)
                ? foundUpdates
                : new List<ActionTaskUpdate>();
            var assignedTaskUserId = string.IsNullOrWhiteSpace(row.AssignedToUserId)
                ? officer.UserId
                : row.AssignedToUserId;
            var latestAssigneeUpdate = direction is null
                ? null
                : itemUpdates
                    .Where(update => string.Equals(
                            update.CreatedByUserId,
                            assignedTaskUserId,
                            StringComparison.Ordinal)
                        && IsAfter(update.CreatedAtUtc, update.Id, direction.CreatedAtUtc, direction.Id))
                    .OrderByDescending(update => update.CreatedAtUtc)
                    .ThenByDescending(update => update.Id)
                    .FirstOrDefault();

            var progressEntries = new List<ConferenceProgressEntryVm>();
            if (direction is not null)
            {
                progressEntries.Add(latestAssigneeUpdate is null
                    ? new ConferenceProgressEntryVm
                    {
                        Label = "Task Assignee",
                        EmptyText = "No update by the task assignee after the direction."
                    }
                    : new ConferenceProgressEntryVm
                    {
                        Label = "Task Assignee",
                        Body = ConferenceDirectionTextFormatter.ToDisplayText(latestAssigneeUpdate.Body),
                        AuthorName = ResolveAuthor(authorNames, latestAssigneeUpdate.CreatedByUserId),
                        ActivityAtUtc = AsUtc(latestAssigneeUpdate.CreatedAtUtc)
                    });
            }

            result.Add(new OfficerConferenceItemVm
            {
                Kind = ConferenceItemKind.ActionTask,
                ItemId = row.Id,
                Title = row.Title,
                OpenUrl = workloadItem.OpenUrl,
                CurrentStateCode = row.Status,
                CurrentStateName = row.Status,
                CurrentContext = $"Due {dueDate:dd MMM yyyy} · {row.Priority} priority",
                AttentionText = attentionText,
                RequiresAttention = requiresAttention,
                LatestDirection = direction is null
                    ? null
                    : new ConferenceDirectionVm
                    {
                        Id = direction.Id,
                        Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
                        AuthorName = ResolveAuthor(authorNames, direction.CreatedByUserId),
                        AuthorRole = DisplayRole(direction.CreatedByRole),
                        CreatedAtUtc = AsUtc(direction.CreatedAtUtc),
                        SnapshotLabel = "When issued",
                        SnapshotValue = BuildTaskSnapshot(direction.StatusSnapshot, direction.DueDateSnapshot)
                    },
                ProgressEntries = progressEntries,
                EmptyProgressText = null,
                ProgressSummary = string.Empty,
                LatestProgressText = null
            });
        }

        return result
            .OrderBy(item => TaskAttentionOrder(rowsById[item.ItemId], today))
            .ThenBy(item => rowsById[item.ItemId].DueDate)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ConferenceProgressEntryVm BuildRemarkProgressEntry(
        string label,
        Remark remark,
        IReadOnlyDictionary<string, string> authorNames)
        => new()
        {
            Label = label,
            Body = ConferenceDirectionTextFormatter.ToDisplayText(remark.Body),
            AuthorName = ResolveAuthor(authorNames, remark.AuthorUserId),
            ActivityAtUtc = AsUtc(remark.CreatedAtUtc)
        };

    private static DateTime NoteActivityAt(IdeaNoteRow note)
        => note.UpdatedAt > note.CreatedAt ? note.UpdatedAt : note.CreatedAt;

    private static int TaskAttentionOrder(TaskRow row, DateOnly today)
    {
        var due = DateOnly.FromDateTime(row.DueDate);
        if (string.Equals(row.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)) return 0;
        if (due < today) return 1;
        if (string.Equals(row.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)) return 2;
        if (due.DayNumber - today.DayNumber <= 7) return 3;
        return 4;
    }

    private static bool IsAfter(
        DateTime candidateAt,
        int candidateId,
        DateTime baselineAt,
        int baselineId)
        => candidateAt > baselineAt
           || (candidateAt == baselineAt && candidateId > baselineId);

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

    private static string DisplayRole(RemarkActorRole role) => role switch
    {
        RemarkActorRole.Commandant => "Comdt",
        RemarkActorRole.HeadOfDepartment => "HoD",
        RemarkActorRole.ProjectOfficer => "Project Officer",
        RemarkActorRole.Administrator => "Admin",
        RemarkActorRole.Ta => "TA",
        RemarkActorRole.Mco => "MCO",
        RemarkActorRole.ProjectOffice => "Project Office",
        RemarkActorRole.MainOffice => "Main Office",
        _ => "User"
    };

    private static string DisplayRole(string? role)
    {
        if (string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)) return "Comdt";
        if (string.Equals(role, RoleNames.HoD, StringComparison.OrdinalIgnoreCase)) return "HoD";
        return string.IsNullOrWhiteSpace(role) ? "User" : role.Trim();
    }

    private static string ResolveAuthor(
        IReadOnlyDictionary<string, string> authors,
        string userId)
        => authors.TryGetValue(userId, out var name) ? name : userId;

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string DisplayOfficerName(CommandOfficerWorkloadVm officer)
        => string.IsNullOrWhiteSpace(officer.Rank)
            ? officer.OfficerName
            : $"{officer.Rank} {officer.OfficerName}";

    private static string InitialOf(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? "P"
            : name.Trim()[0].ToString().ToUpperInvariant();

    private sealed record ProjectRow(
        int Id,
        string Name,
        string? LeadPoUserId,
        string WorkflowVersion,
        IReadOnlyList<ProjectStageRow> Stages);

    private sealed record ProjectStageRow(
        string StageCode,
        StageStatus Status,
        int SortOrder,
        DateOnly? ActualStart,
        DateOnly? CompletedOn,
        DateOnly? PlannedDue);

    private sealed record IdeaRow(
        int Id,
        string Title,
        string Status,
        DateTime UpdatedAt);

    private sealed record IdeaNoteRow(
        int Id,
        int ProjectIdeaId,
        string Title,
        string Body,
        string CreatedByUserId,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record TaskRow(
        int Id,
        string Title,
        string Status,
        DateTime DueDate,
        string Priority,
        string AssignedToUserId);
}
