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
using ProjectManagement.Services.Usage;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Builds the canonical conference review from the same project, idea and action-task
/// records used by the operational modules. Command and Project Officer surfaces consume
/// the same read model; the self-service PO entry point fixes the subject to the requester.
/// All source data is loaded in bounded batch queries and conference directions remain
/// native records in their respective modules.
/// </summary>
public sealed class OfficerConferenceReadService : IOfficerConferenceReadService
{
    private readonly ApplicationDbContext _db;
    private readonly IOfficerWorkloadReadService _workload;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
    private readonly IClock _clock;
    private readonly ProjectRecordHealthService? _recordHealth;
    private readonly IErpUsageQueryService? _erpUsage;
    private readonly IConferenceProjectScopeService? _projectScope;

    // Retained for isolated conference-read tests and legacy composition roots. The
    // application DI container resolves the complete constructor below.
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

    public OfficerConferenceReadService(
        ApplicationDbContext db,
        IOfficerWorkloadReadService workload,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IClock clock,
        IConferenceProjectScopeService projectScope)
        : this(db, workload, workflowStageMetadataProvider, clock)
    {
        _projectScope = projectScope ?? throw new ArgumentNullException(nameof(projectScope));
    }

    public OfficerConferenceReadService(
        ApplicationDbContext db,
        IOfficerWorkloadReadService workload,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IClock clock,
        ProjectRecordHealthService recordHealth,
        IErpUsageQueryService erpUsage)
        : this(db, workload, workflowStageMetadataProvider, clock)
    {
        _recordHealth = recordHealth ?? throw new ArgumentNullException(nameof(recordHealth));
        _erpUsage = erpUsage ?? throw new ArgumentNullException(nameof(erpUsage));
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public OfficerConferenceReadService(
        ApplicationDbContext db,
        IOfficerWorkloadReadService workload,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IClock clock,
        ProjectRecordHealthService recordHealth,
        IErpUsageQueryService erpUsage,
        IConferenceProjectScopeService projectScope)
        : this(db, workload, workflowStageMetadataProvider, clock, recordHealth, erpUsage)
    {
        _projectScope = projectScope ?? throw new ArgumentNullException(nameof(projectScope));
    }


    public async Task<IReadOnlyList<OfficerConferenceOfficerOptionVm>> GetOfficerOptionsAsync(
        string requestingUserId,
        string? selectedOfficerUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestingUserId))
        {
            return Array.Empty<OfficerConferenceOfficerOptionVm>();
        }

        var orderedOfficers = await LoadConferenceOfficersAsync(requestingUserId, cancellationToken);
        return BuildOfficerOptions(orderedOfficers, selectedOfficerUserId);
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

        var orderedOfficers = await LoadConferenceOfficersAsync(requestingUserId, cancellationToken);
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

        return await BuildConferenceAsync(
            selectedIndex.officer,
            orderedOfficers,
            selectedIndex.index,
            includeOfficerNavigation: true,
            cancellationToken);
    }

    public async Task<OfficerConferenceVm?> GetForProjectOfficerAsync(
        string projectOfficerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectOfficerUserId))
        {
            return null;
        }

        var selected = await LoadProjectOfficerContextAsync(
            projectOfficerUserId,
            cancellationToken);
        if (selected is null)
        {
            return null;
        }

        var selfOnly = new[] { selected };
        return await BuildConferenceAsync(
            selected,
            selfOnly,
            selectedIndex: 0,
            includeOfficerNavigation: false,
            cancellationToken);
    }

    public async Task<ConferenceDirectionDigestVm?> GetLatestDirectionDigestAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestingUserId)
            || !await HasCommandConferenceRoleAsync(requestingUserId, cancellationToken))
        {
            return null;
        }

        var officers = await LoadConferenceOfficersAsync(requestingUserId, cancellationToken);
        if (officers.Count == 0)
        {
            return new ConferenceDirectionDigestVm();
        }

        var projectIds = officers
            .SelectMany(officer => officer.ActiveProjects.Select(project => project.ProjectId)
                .Concat(officer.RecentlyCompletedProjects.Select(project => project.ProjectId)))
            .Distinct()
            .ToArray();
        var ideaIds = officers
            .SelectMany(officer => officer.Ideas.Select(idea => idea.IdeaId))
            .Distinct()
            .ToArray();
        var taskIds = officers
            .SelectMany(officer => officer.OtherTasks.Select(task => task.TaskId))
            .Distinct()
            .ToArray();

        // The scoped DbContext must not execute concurrent operations. These three
        // bounded queries deliberately run sequentially.
        var latestProjectDirections = (await LoadLatestProjectDirectionsAsync(projectIds, cancellationToken))
            .ToDictionary(direction => direction.ProjectId);
        var latestIdeaDirections = (await LoadLatestIdeaDirectionsAsync(ideaIds, cancellationToken))
            .ToDictionary(direction => direction.ProjectIdeaId);
        var latestTaskDirections = (await LoadLatestTaskDirectionsAsync(taskIds, cancellationToken))
            .ToDictionary(direction => direction.TaskId);

        var groups = new List<ConferenceDirectionDigestOfficerVm>();
        foreach (var officer in officers)
        {
            var items = new List<ConferenceDirectionDigestItemVm>();

            var projectSources = officer.ActiveProjects
                .Select(project => new
                {
                    project.ProjectId,
                    project.Name,
                    project.OpenUrl
                })
                .Concat(officer.RecentlyCompletedProjects.Select(project => new
                {
                    project.ProjectId,
                    Name = project.ProjectName,
                    OpenUrl = $"/Projects/Overview/{project.ProjectId}"
                }))
                .GroupBy(project => project.ProjectId)
                .Select(group => group.First());

            foreach (var project in projectSources)
            {
                if (!latestProjectDirections.TryGetValue(project.ProjectId, out var direction))
                {
                    continue;
                }

                items.Add(new ConferenceDirectionDigestItemVm
                {
                    Kind = ConferenceItemKind.Project,
                    ItemId = project.ProjectId,
                    Title = project.Name,
                    DirectionText = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
                    IssuedAtUtc = AsUtc(direction.CreatedAtUtc),
                    OpenUrl = project.OpenUrl
                });
            }

            foreach (var idea in officer.Ideas)
            {
                if (!latestIdeaDirections.TryGetValue(idea.IdeaId, out var direction))
                {
                    continue;
                }

                items.Add(new ConferenceDirectionDigestItemVm
                {
                    Kind = ConferenceItemKind.ProjectIdea,
                    ItemId = idea.IdeaId,
                    Title = idea.Title,
                    DirectionText = ConferenceDirectionTextFormatter.ToDisplayText(direction.CommentText),
                    IssuedAtUtc = AsUtc(direction.CreatedAt),
                    OpenUrl = idea.OpenUrl
                });
            }

            foreach (var task in officer.OtherTasks)
            {
                if (!latestTaskDirections.TryGetValue(task.TaskId, out var direction))
                {
                    continue;
                }

                items.Add(new ConferenceDirectionDigestItemVm
                {
                    Kind = ConferenceItemKind.ActionTask,
                    ItemId = task.TaskId,
                    Title = task.Title,
                    DirectionText = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
                    IssuedAtUtc = AsUtc(direction.CreatedAtUtc),
                    OpenUrl = task.OpenUrl
                });
            }

            var orderedItems = items
                .OrderByDescending(item => item.IssuedAtUtc)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (orderedItems.Length == 0)
            {
                continue;
            }

            groups.Add(new ConferenceDirectionDigestOfficerVm
            {
                OfficerUserId = officer.UserId,
                OfficerDisplayName = DisplayOfficerName(officer),
                ConferenceReviewUrl = $"/Workspace/Conference/{officer.UserId}",
                LatestDirectionAtUtc = orderedItems[0].IssuedAtUtc,
                Directions = orderedItems
            });
        }

        var orderedGroups = groups
            .OrderByDescending(group => group.LatestDirectionAtUtc)
            .ThenBy(group => group.OfficerDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ConferenceDirectionDigestVm
        {
            TotalDirectionCount = orderedGroups.Sum(group => group.Directions.Count),
            OfficerGroups = orderedGroups
        };
    }

    private async Task<OfficerConferenceVm> BuildConferenceAsync(
        ConferenceOfficerContext selected,
        IReadOnlyList<ConferenceOfficerContext> orderedOfficers,
        int selectedIndex,
        bool includeOfficerNavigation,
        CancellationToken cancellationToken)
    {
        var projectIds = selected.ActiveProjects
            .Select(item => item.ProjectId)
            .Concat(selected.RecentlyCompletedProjects.Select(item => item.ProjectId))
            .Distinct()
            .ToArray();
        var ideaIds = selected.Ideas.Select(item => item.IdeaId).Distinct().ToArray();
        var taskIds = selected.OtherTasks.Select(item => item.TaskId).Distinct().ToArray();

        // A scoped DbContext does not support concurrent operations. Keep the query set
        // bounded and batched, but execute it sequentially to preserve EF Core safety.
        var projectRows = await LoadProjectsAsync(projectIds, cancellationToken);
        var ideaRows = await LoadIdeasAsync(ideaIds, cancellationToken);
        var taskRows = await LoadTasksAsync(taskIds, cancellationToken);
        var projectRecordHealth = _recordHealth is null
            ? new Dictionary<int, WorkspaceRecordHealthVm>()
            : await _recordHealth.CalculateForProjectIdsAsync(
                projectIds,
                selected.UserId,
                cancellationToken);
        var activityStrip = _erpUsage is null
            ? new ErpActivityStripVm()
            : await _erpUsage.GetActivityStripAsync(
                selected.UserId,
                days: 30,
                cancellationToken: cancellationToken);

        var latestProjectDirections = (await LoadLatestProjectDirectionsAsync(projectIds, cancellationToken))
            .ToDictionary(direction => direction.ProjectId);
        var latestIdeaDirections = (await LoadLatestIdeaDirectionsAsync(ideaIds, cancellationToken))
            .ToDictionary(direction => direction.ProjectIdeaId);
        var latestTaskDirections = (await LoadLatestTaskDirectionsAsync(taskIds, cancellationToken))
            .ToDictionary(direction => direction.TaskId);
        var projectDirectionCounts = await LoadProjectDirectionCountsAsync(projectIds, cancellationToken);
        var ideaDirectionCounts = await LoadIdeaDirectionCountsAsync(ideaIds, cancellationToken);
        var taskDirectionCounts = await LoadTaskDirectionCountsAsync(taskIds, cancellationToken);

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
        var ideaComments = await LoadIdeaProgressCommentsAsync(
            latestIdeaDirections,
            selected.UserId,
            cancellationToken);
        var ideaNotes = await LoadIdeaProgressNotesAsync(
            latestIdeaDirections,
            selected.UserId,
            cancellationToken);
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
            projectDirectionCounts,
            authorNames,
            mcoUserIds,
            projectRecordHealth,
            today);
        var ideaItems = BuildIdeaItems(
            selected,
            ideaRows,
            ideaComments,
            ideaNotes,
            latestIdeaDirections,
            ideaDirectionCounts,
            authorNames);
        var taskItems = BuildTaskItems(
            selected,
            taskRows,
            taskUpdates,
            latestTaskDirections,
            taskDirectionCounts,
            authorNames,
            today);

        var officerOptions = BuildOfficerOptions(orderedOfficers, selected.UserId);

        return new OfficerConferenceVm
        {
            OfficerUserId = selected.UserId,
            OfficerName = selected.OfficerName,
            OfficerRank = selected.Rank,
            OfficerInitial = InitialOf(selected.OfficerName),
            ProjectCount = projectItems.Count,
            ActiveProjectCount = selected.ActiveProjects.Count,
            RecentlyCompletedProjectCount = selected.RecentlyCompletedProjects.Count,
            IdeaCount = ideaItems.Count,
            OtherTaskCount = taskItems.Count,
            CompletedProjectRetentionDays = _projectScope?.CompletedProjectRetentionDays ?? 0,
            PreviousOfficerUserId = includeOfficerNavigation && selectedIndex > 0
                ? orderedOfficers[selectedIndex - 1].UserId
                : null,
            NextOfficerUserId = includeOfficerNavigation && selectedIndex + 1 < orderedOfficers.Count
                ? orderedOfficers[selectedIndex + 1].UserId
                : null,
            OfficerOptions = officerOptions,
            ActivityStrip = activityStrip,
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


    public async Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryAsync(
        string requestingUserId,
        string officerUserId,
        ConferenceItemKind kind,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestingUserId)
            || string.IsNullOrWhiteSpace(officerUserId)
            || itemId <= 0
            || !Enum.IsDefined(kind))
        {
            return null;
        }

        var orderedOfficers = await LoadConferenceOfficersAsync(requestingUserId, cancellationToken);
        var selected = orderedOfficers.FirstOrDefault(officer => string.Equals(
            officer.UserId,
            officerUserId,
            StringComparison.Ordinal));
        if (selected is null)
        {
            return null;
        }

        var projectInScope = kind != ConferenceItemKind.Project
            || (_projectScope is not null
                ? await _projectScope.IsProjectInScopeAsync(officerUserId, itemId, cancellationToken)
                : selected.ActiveProjects.Any(item => item.ProjectId == itemId));

        return kind switch
        {
            ConferenceItemKind.Project when projectInScope
                => await BuildProjectDirectionHistoryAsync(selected.UserId, itemId, cancellationToken),
            ConferenceItemKind.ProjectIdea when selected.Ideas.Any(item => item.IdeaId == itemId)
                => await BuildIdeaDirectionHistoryAsync(selected.UserId, itemId, cancellationToken),
            ConferenceItemKind.ActionTask when selected.OtherTasks.Any(item => item.TaskId == itemId)
                => await BuildTaskDirectionHistoryAsync(selected.UserId, itemId, cancellationToken),
            _ => null
        };
    }

    public async Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryForProjectOfficerAsync(
        string projectOfficerUserId,
        ConferenceItemKind kind,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectOfficerUserId)
            || itemId <= 0
            || !Enum.IsDefined(kind))
        {
            return null;
        }

        var selected = await LoadProjectOfficerContextAsync(
            projectOfficerUserId,
            cancellationToken);
        if (selected is null)
        {
            return null;
        }

        var projectInScope = kind != ConferenceItemKind.Project
            || (_projectScope is not null
                ? await _projectScope.IsProjectInScopeAsync(projectOfficerUserId, itemId, cancellationToken)
                : selected.ActiveProjects.Any(item => item.ProjectId == itemId));

        return kind switch
        {
            ConferenceItemKind.Project when projectInScope
                => await BuildProjectDirectionHistoryAsync(selected.UserId, itemId, cancellationToken),
            ConferenceItemKind.ProjectIdea when selected.Ideas.Any(item => item.IdeaId == itemId)
                => await BuildIdeaDirectionHistoryAsync(selected.UserId, itemId, cancellationToken),
            ConferenceItemKind.ActionTask when selected.OtherTasks.Any(item => item.TaskId == itemId)
                => await BuildTaskDirectionHistoryAsync(selected.UserId, itemId, cancellationToken),
            _ => null
        };
    }

    private async Task<ConferenceDirectionHistoryVm> BuildProjectDirectionHistoryAsync(
        string officerUserId,
        int projectId,
        CancellationToken cancellationToken)
    {
        var project = (await LoadProjectsAsync(new[] { projectId }, cancellationToken)).SingleOrDefault();
        if (project is null)
        {
            return EmptyHistory(ConferenceItemKind.Project, projectId);
        }

        var directions = await _db.Remarks
            .AsNoTracking()
            .Where(direction => direction.ProjectId == projectId
                && !direction.IsDeleted
                && direction.Type == RemarkType.Conference)
            .OrderBy(direction => direction.CreatedAtUtc)
            .ThenBy(direction => direction.Id)
            .ToListAsync(cancellationToken);
        if (directions.Count == 0)
        {
            return EmptyHistory(ConferenceItemKind.Project, projectId);
        }

        var assignedProjectOfficerId = string.IsNullOrWhiteSpace(project.LeadPoUserId)
            ? officerUserId
            : project.LeadPoUserId;
        var mcoUserIds = await LoadUserIdsInRoleAsync(RoleNames.Mco, cancellationToken);
        var mcoUserIdArray = mcoUserIds.ToArray();
        var firstDirection = directions[0];
        var remarks = await _db.Remarks
            .AsNoTracking()
            .Where(remark => remark.ProjectId == projectId
                && !remark.IsDeleted
                && remark.Type != RemarkType.Conference
                && (remark.AuthorUserId == assignedProjectOfficerId
                    || remark.AuthorRole == RemarkActorRole.Mco
                    || mcoUserIdArray.Contains(remark.AuthorUserId))
                && remark.CreatedAtUtc >= firstDirection.CreatedAtUtc)
            .OrderBy(remark => remark.CreatedAtUtc)
            .ThenBy(remark => remark.Id)
            .ToListAsync(cancellationToken);

        var authorNames = await LoadAuthorNamesAsync(
            directions.Select(direction => direction.AuthorUserId)
                .Concat(remarks.Select(remark => remark.AuthorUserId)),
            cancellationToken);
        var cycles = new List<ConferenceDirectionCycleVm>(directions.Count);

        for (var index = 0; index < directions.Count; index++)
        {
            var direction = directions[index];
            var nextDirection = index + 1 < directions.Count ? directions[index + 1] : null;
            var cycleRemarks = remarks
                .Where(remark => IsAfter(
                        remark.CreatedAtUtc,
                        remark.Id,
                        direction.CreatedAtUtc,
                        direction.Id)
                    && (nextDirection is null || IsBefore(
                        remark.CreatedAtUtc,
                        remark.Id,
                        nextDirection.CreatedAtUtc,
                        nextDirection.Id)))
                .ToList();

            var latestProjectOfficerRemark = cycleRemarks
                .Where(remark => string.Equals(
                    remark.AuthorUserId,
                    assignedProjectOfficerId,
                    StringComparison.Ordinal))
                .OrderByDescending(remark => remark.CreatedAtUtc)
                .ThenByDescending(remark => remark.Id)
                .FirstOrDefault();
            var latestMcoRemark = cycleRemarks
                .Where(remark => (remark.AuthorRole == RemarkActorRole.Mco
                        || mcoUserIds.Contains(remark.AuthorUserId))
                    && !string.Equals(
                        remark.AuthorUserId,
                        assignedProjectOfficerId,
                        StringComparison.Ordinal))
                .OrderByDescending(remark => remark.CreatedAtUtc)
                .ThenByDescending(remark => remark.Id)
                .FirstOrDefault();

            var progressEntries = new List<ConferenceProgressEntryVm>
            {
                latestProjectOfficerRemark is null
                    ? new ConferenceProgressEntryVm
                    {
                        Label = "Project Officer",
                        EmptyText = nextDirection is null
                            ? "Progress update awaited. No Project Officer remark has been recorded since the direction was issued."
                            : "No Project Officer remark was recorded before the next conference direction."
                    }
                    : BuildRemarkProgressEntry(
                        "Project Officer",
                        latestProjectOfficerRemark,
                        authorNames)
            };
            if (latestMcoRemark is not null)
            {
                progressEntries.Add(BuildRemarkProgressEntry("MCO", latestMcoRemark, authorNames));
            }

            cycles.Add(new ConferenceDirectionCycleVm
            {
                Direction = new ConferenceDirectionVm
                {
                    Id = direction.Id,
                    Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
                    AuthorName = ResolveAuthor(authorNames, direction.AuthorUserId),
                    AuthorRole = DisplayRole(direction.AuthorRole),
                    CreatedAtUtc = AsUtc(direction.CreatedAtUtc),
                    SnapshotLabel = BuildProjectSnapshotLabel(direction.StageRef, direction.StageNameSnapshot),
                    SnapshotValue = BuildStageSnapshot(direction.StageRef, direction.StageNameSnapshot)
                },
                ProgressEntries = progressEntries,
                SequenceNumber = index + 1,
                TotalDirections = directions.Count,
                IsLatest = index == directions.Count - 1
            });
        }

        return new ConferenceDirectionHistoryVm
        {
            Kind = ConferenceItemKind.Project,
            ItemId = projectId,
            Cycles = cycles
        };
    }

    private async Task<ConferenceDirectionHistoryVm> BuildIdeaDirectionHistoryAsync(
        string officerUserId,
        int ideaId,
        CancellationToken cancellationToken)
    {
        var idea = (await LoadIdeasAsync(new[] { ideaId }, cancellationToken)).SingleOrDefault();
        if (idea is null)
        {
            return EmptyHistory(ConferenceItemKind.ProjectIdea, ideaId);
        }

        var directions = await _db.ProjectIdeaComments
            .AsNoTracking()
            .Where(direction => direction.ProjectIdeaId == ideaId
                && !direction.IsDeleted
                && direction.CommentType == ProjectIdeaCommentTypes.Conference)
            .OrderBy(direction => direction.CreatedAt)
            .ThenBy(direction => direction.Id)
            .ToListAsync(cancellationToken);
        if (directions.Count == 0)
        {
            return EmptyHistory(ConferenceItemKind.ProjectIdea, ideaId);
        }

        var firstDirection = directions[0];
        var comments = await _db.ProjectIdeaComments
            .AsNoTracking()
            .Where(comment => comment.ProjectIdeaId == ideaId
                && !comment.IsDeleted
                && comment.CommentType != ProjectIdeaCommentTypes.Conference
                && comment.CreatedByUserId == officerUserId
                && comment.CreatedAt >= firstDirection.CreatedAt)
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .ToListAsync(cancellationToken);
        var notes = await _db.ProjectIdeaNotes
            .AsNoTracking()
            .Where(note => note.ProjectIdeaId == ideaId
                && !note.IsDeleted
                && note.CreatedByUserId == officerUserId
                && (note.CreatedAt >= firstDirection.CreatedAt
                    || note.UpdatedAt >= firstDirection.CreatedAt))
            .Select(note => new IdeaNoteRow(
                note.Id,
                note.ProjectIdeaId,
                note.Title,
                note.Body,
                note.CreatedByUserId,
                note.CreatedAt,
                note.UpdatedAt))
            .ToListAsync(cancellationToken);

        var authorNames = await LoadAuthorNamesAsync(
            directions.Select(direction => direction.CreatedByUserId)
                .Concat(comments.Select(comment => comment.CreatedByUserId))
                .Concat(notes.Select(note => note.CreatedByUserId)),
            cancellationToken);
        var cycles = new List<ConferenceDirectionCycleVm>(directions.Count);

        for (var index = 0; index < directions.Count; index++)
        {
            var direction = directions[index];
            var nextDirection = index + 1 < directions.Count ? directions[index + 1] : null;
            var latestComment = comments
                .Where(comment => IsAfter(
                        comment.CreatedAt,
                        comment.Id,
                        direction.CreatedAt,
                        direction.Id)
                    && (nextDirection is null || IsBefore(
                        comment.CreatedAt,
                        comment.Id,
                        nextDirection.CreatedAt,
                        nextDirection.Id)))
                .OrderByDescending(comment => comment.CreatedAt)
                .ThenByDescending(comment => comment.Id)
                .FirstOrDefault();
            var latestNote = notes
                .Where(note => NoteActivityAt(note) > direction.CreatedAt
                    && (nextDirection is null || NoteActivityAt(note) < nextDirection.CreatedAt))
                .OrderByDescending(NoteActivityAt)
                .ThenByDescending(note => note.Id)
                .FirstOrDefault();

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

            cycles.Add(new ConferenceDirectionCycleVm
            {
                Direction = new ConferenceDirectionVm
                {
                    Id = direction.Id,
                    Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.CommentText),
                    AuthorName = ResolveAuthor(authorNames, direction.CreatedByUserId),
                    AuthorRole = DisplayRole(direction.CreatedByRole),
                    CreatedAtUtc = AsUtc(direction.CreatedAt),
                    SnapshotLabel = "Status when issued",
                    SnapshotValue = ProjectIdeaStatuses.ToDisplay(direction.StatusSnapshot ?? idea.Status)
                },
                ProgressEntries = progressEntries,
                EmptyProgressText = progressEntries.Count == 0
                    ? nextDirection is null
                        ? "Progress update awaited. No Project Officer comment or note has been recorded since the direction was issued."
                        : "No Project Officer comment or note was recorded before the next conference direction."
                    : null,
                SequenceNumber = index + 1,
                TotalDirections = directions.Count,
                IsLatest = index == directions.Count - 1
            });
        }

        return new ConferenceDirectionHistoryVm
        {
            Kind = ConferenceItemKind.ProjectIdea,
            ItemId = ideaId,
            Cycles = cycles
        };
    }

    private async Task<ConferenceDirectionHistoryVm> BuildTaskDirectionHistoryAsync(
        string officerUserId,
        int taskId,
        CancellationToken cancellationToken)
    {
        var task = (await LoadTasksAsync(new[] { taskId }, cancellationToken)).SingleOrDefault();
        if (task is null)
        {
            return EmptyHistory(ConferenceItemKind.ActionTask, taskId);
        }

        var directions = await _db.ActionTaskUpdates
            .AsNoTracking()
            .Where(direction => direction.TaskId == taskId
                && !direction.IsDeleted
                && direction.UpdateType == ActionTaskUpdateTypes.Conference)
            .OrderBy(direction => direction.CreatedAtUtc)
            .ThenBy(direction => direction.Id)
            .ToListAsync(cancellationToken);
        if (directions.Count == 0)
        {
            return EmptyHistory(ConferenceItemKind.ActionTask, taskId);
        }

        var assignedTaskUserId = string.IsNullOrWhiteSpace(task.AssignedToUserId)
            ? officerUserId
            : task.AssignedToUserId;
        var firstDirection = directions[0];
        var updates = await _db.ActionTaskUpdates
            .AsNoTracking()
            .Where(update => update.TaskId == taskId
                && !update.IsDeleted
                && update.UpdateType != ActionTaskUpdateTypes.Conference
                && update.CreatedByUserId == assignedTaskUserId
                && update.CreatedAtUtc >= firstDirection.CreatedAtUtc)
            .OrderBy(update => update.CreatedAtUtc)
            .ThenBy(update => update.Id)
            .ToListAsync(cancellationToken);

        var authorNames = await LoadAuthorNamesAsync(
            directions.Select(direction => direction.CreatedByUserId)
                .Concat(updates.Select(update => update.CreatedByUserId)),
            cancellationToken);
        var cycles = new List<ConferenceDirectionCycleVm>(directions.Count);

        for (var index = 0; index < directions.Count; index++)
        {
            var direction = directions[index];
            var nextDirection = index + 1 < directions.Count ? directions[index + 1] : null;
            var latestUpdate = updates
                .Where(update => IsAfter(
                        update.CreatedAtUtc,
                        update.Id,
                        direction.CreatedAtUtc,
                        direction.Id)
                    && (nextDirection is null || IsBefore(
                        update.CreatedAtUtc,
                        update.Id,
                        nextDirection.CreatedAtUtc,
                        nextDirection.Id)))
                .OrderByDescending(update => update.CreatedAtUtc)
                .ThenByDescending(update => update.Id)
                .FirstOrDefault();

            var progressEntries = new List<ConferenceProgressEntryVm>
            {
                latestUpdate is null
                    ? new ConferenceProgressEntryVm
                    {
                        Label = "Task Assignee",
                        EmptyText = nextDirection is null
                            ? "Progress update awaited. No task-assignee update has been recorded since the direction was issued."
                            : "No task-assignee update was recorded before the next conference direction."
                    }
                    : new ConferenceProgressEntryVm
                    {
                        Label = "Task Assignee",
                        Body = ConferenceDirectionTextFormatter.ToDisplayText(latestUpdate.Body),
                        AuthorName = ResolveAuthor(authorNames, latestUpdate.CreatedByUserId),
                        ActivityAtUtc = AsUtc(latestUpdate.CreatedAtUtc)
                    }
            };

            cycles.Add(new ConferenceDirectionCycleVm
            {
                Direction = new ConferenceDirectionVm
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
                SequenceNumber = index + 1,
                TotalDirections = directions.Count,
                IsLatest = index == directions.Count - 1
            });
        }

        return new ConferenceDirectionHistoryVm
        {
            Kind = ConferenceItemKind.ActionTask,
            ItemId = taskId,
            Cycles = cycles
        };
    }

    private async Task<Dictionary<string, string>> LoadAuthorNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var authors = await _db.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new
            {
                user.Id,
                Name = string.IsNullOrWhiteSpace(user.FullName)
                    ? user.UserName ?? user.Id
                    : user.FullName
            })
            .ToListAsync(cancellationToken);
        return authors.ToDictionary(author => author.Id, author => author.Name, StringComparer.Ordinal);
    }

    private static ConferenceDirectionHistoryVm EmptyHistory(ConferenceItemKind kind, int itemId)
        => new()
        {
            Kind = kind,
            ItemId = itemId,
            Cycles = Array.Empty<ConferenceDirectionCycleVm>()
        };


    private async Task<ConferenceOfficerContext?> LoadProjectOfficerContextAsync(
        string projectOfficerUserId,
        CancellationToken cancellationToken)
    {
        var workload = await _workload.GetOfficerAsync(projectOfficerUserId, cancellationToken);
        IReadOnlyList<ConferenceProjectCarryover> carryovers = _projectScope is null
            ? Array.Empty<ConferenceProjectCarryover>()
            : (await _projectScope.GetRecentlyCompletedProjectsAsync(cancellationToken))
                .Where(item => string.Equals(
                    item.OfficerUserId,
                    projectOfficerUserId,
                    StringComparison.Ordinal))
                .OrderByDescending(item => item.CompletionSortDate)
                .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (workload is not null)
        {
            return new ConferenceOfficerContext(
                workload.UserId,
                workload.OfficerName,
                workload.Rank,
                workload.Projects,
                workload.Ideas,
                workload.OtherTasks,
                carryovers);
        }

        if (carryovers.Count > 0)
        {
            var identity = carryovers[0];
            return new ConferenceOfficerContext(
                identity.OfficerUserId,
                identity.OfficerName,
                identity.OfficerRank,
                Array.Empty<CommandOfficerProjectVm>(),
                Array.Empty<CommandOfficerIdeaVm>(),
                Array.Empty<CommandOfficerTaskVm>(),
                carryovers);
        }

        // Keep the self-service conference surface available even when the officer has
        // no current workload. The page can then show an intentional empty state rather
        // than turning a valid Project Officer workspace route into a 404.
        var identityRow = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == projectOfficerUserId
                && !user.IsDisabled
                && !user.PendingDeletion)
            .Select(user => new
            {
                user.Id,
                Name = string.IsNullOrWhiteSpace(user.FullName)
                    ? user.UserName ?? "Project Officer"
                    : user.FullName,
                user.Rank
            })
            .SingleOrDefaultAsync(cancellationToken);

        return identityRow is null
            ? null
            : new ConferenceOfficerContext(
                identityRow.Id,
                identityRow.Name,
                identityRow.Rank ?? string.Empty,
                Array.Empty<CommandOfficerProjectVm>(),
                Array.Empty<CommandOfficerIdeaVm>(),
                Array.Empty<CommandOfficerTaskVm>(),
                Array.Empty<ConferenceProjectCarryover>());
    }

    private async Task<IReadOnlyList<ConferenceOfficerContext>> LoadConferenceOfficersAsync(
        string requestingUserId,
        CancellationToken cancellationToken)
    {
        var activeWorkloads = await _workload.GetAllAsync(requestingUserId, cancellationToken);
        IReadOnlyList<ConferenceProjectCarryover> carryovers = _projectScope is null
            ? Array.Empty<ConferenceProjectCarryover>()
            : await _projectScope.GetRecentlyCompletedProjectsAsync(cancellationToken);
        var carryoversByOfficer = carryovers
            .GroupBy(item => item.OfficerUserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConferenceProjectCarryover>)group
                    .OrderByDescending(item => item.CompletionSortDate)
                    .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);

        var result = new List<ConferenceOfficerContext>(
            activeWorkloads.Count + carryoversByOfficer.Count);
        var includedOfficerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var workload in activeWorkloads)
        {
            carryoversByOfficer.TryGetValue(workload.UserId, out var officerCarryovers);
            officerCarryovers ??= Array.Empty<ConferenceProjectCarryover>();
            result.Add(new ConferenceOfficerContext(
                workload.UserId,
                workload.OfficerName,
                workload.Rank,
                workload.Projects,
                workload.Ideas,
                workload.OtherTasks,
                officerCarryovers));
            includedOfficerIds.Add(workload.UserId);
        }

        var carryoverOnlyOfficers = carryovers
            .Where(item => !includedOfficerIds.Contains(item.OfficerUserId))
            .GroupBy(item => item.OfficerUserId, StringComparer.Ordinal)
            .Select(group =>
            {
                var identity = group.First();
                return new ConferenceOfficerContext(
                    identity.OfficerUserId,
                    identity.OfficerName,
                    identity.OfficerRank,
                    Array.Empty<CommandOfficerProjectVm>(),
                    Array.Empty<CommandOfficerIdeaVm>(),
                    Array.Empty<CommandOfficerTaskVm>(),
                    group
                        .OrderByDescending(item => item.CompletionSortDate)
                        .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderBy(officer => OfficerRankOrder(officer.Rank))
            .ThenBy(officer => officer.OfficerName, StringComparer.OrdinalIgnoreCase);

        result.AddRange(carryoverOnlyOfficers);
        return result;
    }

    private static IReadOnlyList<OfficerConferenceOfficerOptionVm> BuildOfficerOptions(
        IReadOnlyList<ConferenceOfficerContext> orderedOfficers,
        string? selectedOfficerUserId)
        => orderedOfficers
            .Select(officer => new OfficerConferenceOfficerOptionVm(
                officer.UserId,
                DisplayOfficerName(officer),
                string.Equals(officer.UserId, selectedOfficerUserId, StringComparison.Ordinal),
                officer.ProjectCount,
                officer.Ideas.Count,
                officer.OtherTasks.Count,
                officer.ActiveProjects.Count,
                officer.RecentlyCompletedProjects.Count))
            .ToArray();

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
                project.LifecycleStatus,
                project.CompletedOn,
                project.CompletedYear,
                project.CompletedMonth,
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


    private async Task<IReadOnlyDictionary<int, int>> LoadProjectDirectionCountsAsync(
        int[] projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _db.Remarks
            .AsNoTracking()
            .Where(direction => projectIds.Contains(direction.ProjectId)
                && !direction.IsDeleted
                && direction.Type == RemarkType.Conference)
            .GroupBy(direction => direction.ProjectId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ItemId, item => item.Count, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, int>> LoadIdeaDirectionCountsAsync(
        int[] ideaIds,
        CancellationToken cancellationToken)
    {
        if (ideaIds.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _db.ProjectIdeaComments
            .AsNoTracking()
            .Where(direction => ideaIds.Contains(direction.ProjectIdeaId)
                && !direction.IsDeleted
                && direction.CommentType == ProjectIdeaCommentTypes.Conference)
            .GroupBy(direction => direction.ProjectIdeaId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ItemId, item => item.Count, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, int>> LoadTaskDirectionCountsAsync(
        int[] taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _db.ActionTaskUpdates
            .AsNoTracking()
            .Where(direction => taskIds.Contains(direction.TaskId)
                && !direction.IsDeleted
                && direction.UpdateType == ActionTaskUpdateTypes.Conference)
            .GroupBy(direction => direction.TaskId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ItemId, item => item.Count, cancellationToken);
    }

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

    private async Task<bool> HasCommandConferenceRoleAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var normalizedRoles = new[]
        {
            RoleNames.Comdt.ToUpperInvariant(),
            RoleNames.HoD.ToUpperInvariant()
        };

        return await (
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                    && role.NormalizedName != null
                    && normalizedRoles.Contains(role.NormalizedName)
                select userRole.UserId)
            .AnyAsync(cancellationToken);
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
        string projectOfficerUserId,
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
                && comment.CreatedByUserId == projectOfficerUserId
                && comment.CreatedAt >= earliestDirection)
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .ToListAsync(cancellationToken);
    }

    private Task<List<IdeaNoteRow>> LoadIdeaProgressNotesAsync(
        IReadOnlyDictionary<int, ProjectIdeaComment> latestDirections,
        string projectOfficerUserId,
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
                && note.CreatedByUserId == projectOfficerUserId
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
        ConferenceOfficerContext officer,
        IReadOnlyList<ProjectRow> rows,
        IReadOnlyList<Remark> remarks,
        IReadOnlyDictionary<int, Remark> latestDirections,
        IReadOnlyDictionary<int, int> directionCounts,
        IReadOnlyDictionary<string, string> authorNames,
        IReadOnlySet<string> mcoUserIds,
        IReadOnlyDictionary<int, WorkspaceRecordHealthVm> recordHealth,
        DateOnly today)
    {
        var rowsById = rows.ToDictionary(row => row.Id);
        var remarksByProject = remarks
            .GroupBy(remark => remark.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var sources = officer.ActiveProjects
            .Select(item => new ConferenceProjectSource(
                item.ProjectId,
                item.OpenUrl,
                item.StageCode,
                item.StageName,
                IsRecentlyCompleted: false,
                CompletionContext: null))
            .Concat(officer.RecentlyCompletedProjects.Select(item => new ConferenceProjectSource(
                item.ProjectId,
                $"/Projects/Overview/{item.ProjectId}",
                "COMPLETED",
                "Completed",
                IsRecentlyCompleted: true,
                item.CompletionContext)))
            .ToArray();
        var result = new List<OfficerConferenceItemVm>(sources.Length);

        foreach (var source in sources)
        {
            if (!rowsById.TryGetValue(source.ProjectId, out var row))
            {
                continue;
            }

            string currentCode;
            string currentName;
            string? currentContext;
            string? attentionText = null;
            var requiresAttention = false;

            if (source.IsRecentlyCompleted)
            {
                currentCode = "COMPLETED";
                currentName = "Completed";
                currentContext = string.IsNullOrWhiteSpace(source.CompletionContext)
                    ? "Recently completed"
                    : source.CompletionContext;
            }
            else
            {
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
                currentCode = presentStage.CurrentStageCode ?? source.StageCode;
                currentName = presentStage.CurrentStageName ?? source.StageName;
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

                currentContext = contextParts.Count == 0 ? null : string.Join(" · ", contextParts);
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
                        EmptyText = "Progress update awaited. No Project Officer remark has been recorded since the direction was issued."
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
                OpenUrl = source.OpenUrl,
                CurrentStateCode = currentCode,
                CurrentStateName = currentName,
                CurrentContext = currentContext,
                AttentionText = attentionText,
                RequiresAttention = requiresAttention,
                IsRecentlyCompleted = source.IsRecentlyCompleted,
                RecordHealth = recordHealth.GetValueOrDefault(row.Id),
                LatestDirection = direction is null
                    ? null
                    : new ConferenceDirectionVm
                    {
                        Id = direction.Id,
                        Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
                        AuthorName = ResolveAuthor(authorNames, direction.AuthorUserId),
                        AuthorRole = DisplayRole(direction.AuthorRole),
                        CreatedAtUtc = AsUtc(direction.CreatedAtUtc),
                        SnapshotLabel = BuildProjectSnapshotLabel(direction.StageRef, direction.StageNameSnapshot),
                        SnapshotValue = BuildStageSnapshot(direction.StageRef, direction.StageNameSnapshot)
                    },
                DirectionCount = directionCounts.GetValueOrDefault(row.Id),
                ProgressEntries = progressEntries,
                EmptyProgressText = null,
                ProgressSummary = string.Empty,
                LatestProgressText = null
            });
        }

        return result;
    }

    private static IReadOnlyList<OfficerConferenceItemVm> BuildIdeaItems(
        ConferenceOfficerContext officer,
        IReadOnlyList<IdeaRow> rows,
        IReadOnlyList<ProjectIdeaComment> comments,
        IReadOnlyList<IdeaNoteRow> notes,
        IReadOnlyDictionary<int, ProjectIdeaComment> latestDirections,
        IReadOnlyDictionary<int, int> directionCounts,
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
                        && string.Equals(comment.CreatedByUserId, officer.UserId, StringComparison.Ordinal)
                        && IsAfter(comment.CreatedAt, comment.Id, direction.CreatedAt, direction.Id))
                    .OrderByDescending(comment => comment.CreatedAt)
                    .ThenByDescending(comment => comment.Id)
                    .FirstOrDefault();

                latestNote = itemNotes
                    .Where(note => string.Equals(note.CreatedByUserId, officer.UserId, StringComparison.Ordinal)
                        && NoteActivityAt(note) > direction.CreatedAt)
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
                DirectionCount = directionCounts.GetValueOrDefault(row.Id),
                ProgressEntries = progressEntries,
                EmptyProgressText = direction is not null && progressEntries.Count == 0
                    ? "Progress update awaited. No Project Officer comment or note has been recorded since the direction was issued."
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
        ConferenceOfficerContext officer,
        IReadOnlyList<TaskRow> rows,
        IReadOnlyList<ActionTaskUpdate> updates,
        IReadOnlyDictionary<int, ActionTaskUpdate> latestDirections,
        IReadOnlyDictionary<int, int> directionCounts,
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
                        EmptyText = "Progress update awaited. No task-assignee update has been recorded since the direction was issued."
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
                DirectionCount = directionCounts.GetValueOrDefault(row.Id),
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

    private static bool IsBefore(
        DateTime candidateAt,
        int candidateId,
        DateTime boundaryAt,
        int boundaryId)
        => candidateAt < boundaryAt
           || (candidateAt == boundaryAt && candidateId < boundaryId);

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

    private static string DisplayOfficerName(ConferenceOfficerContext officer)
        => string.IsNullOrWhiteSpace(officer.Rank)
            ? officer.OfficerName
            : $"{officer.Rank} {officer.OfficerName}";

    private static int OfficerRankOrder(string? rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return int.MaxValue;
        var value = rank.Trim().ToUpperInvariant();
        if (value.Contains("LT COL", StringComparison.Ordinal)
            || value.Contains("LIEUTENANT COLONEL", StringComparison.Ordinal)) return 20;
        if (value.Contains("COLONEL", StringComparison.Ordinal) || value == "COL") return 10;
        if (value.Contains("MAJOR", StringComparison.Ordinal) || value == "MAJ") return 30;
        if (value.Contains("CAPTAIN", StringComparison.Ordinal) || value == "CAPT") return 40;
        if (value.Contains("LIEUTENANT", StringComparison.Ordinal) || value == "LT") return 50;
        return 100;
    }

    private static string InitialOf(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? "P"
            : name.Trim()[0].ToString().ToUpperInvariant();

    private sealed record ConferenceOfficerContext(
        string UserId,
        string OfficerName,
        string Rank,
        IReadOnlyList<CommandOfficerProjectVm> ActiveProjects,
        IReadOnlyList<CommandOfficerIdeaVm> Ideas,
        IReadOnlyList<CommandOfficerTaskVm> OtherTasks,
        IReadOnlyList<ConferenceProjectCarryover> RecentlyCompletedProjects)
    {
        public int ProjectCount => ActiveProjects.Count + RecentlyCompletedProjects.Count;
    }

    private sealed record ConferenceProjectSource(
        int ProjectId,
        string OpenUrl,
        string StageCode,
        string StageName,
        bool IsRecentlyCompleted,
        string? CompletionContext);

    private sealed record ProjectRow(
        int Id,
        string Name,
        string? LeadPoUserId,
        string WorkflowVersion,
        ProjectLifecycleStatus LifecycleStatus,
        DateOnly? CompletedOn,
        int? CompletedYear,
        short? CompletedMonth,
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
