using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Canonical read service for the officer workload cards. Projects, ideas and action
/// tasks are loaded in batches and normalised into the shared command-workspace model.
/// </summary>
public sealed class OfficerWorkloadReadService : IOfficerWorkloadReadService
{
    private const string UnassignedStageCode = "UNASSIGNED";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;

    public OfficerWorkloadReadService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _workflowStageMetadataProvider = workflowStageMetadataProvider
            ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
    }

    public async Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var roleUsers = await _userManager.GetUsersInRoleAsync(RoleNames.ProjectOfficer);
        var users = roleUsers
            .Where(IsActiveUser)
            .ToList();

        if (users.Count == 0)
        {
            return Array.Empty<CommandOfficerWorkloadVm>();
        }

        var result = await BuildCardsAsync(users, cancellationToken);
        var defaultOrder = result
            .OrderBy(card => RankOrder(card.Rank))
            .ThenBy(card => card.OfficerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await ApplySavedOrderAsync(defaultOrder, requestingUserId, cancellationToken);
    }

    public async Task<CommandOfficerWorkloadVm?> GetOfficerAsync(
        string officerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(officerUserId))
        {
            return null;
        }

        var user = await _userManager.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == officerUserId, cancellationToken);

        if (user is null || !IsActiveUser(user))
        {
            return null;
        }

        var cards = await BuildCardsAsync(new[] { user }, cancellationToken);
        return cards.SingleOrDefault();
    }

    public async Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
    {
        var roleUsers = await _userManager.GetUsersInRoleAsync(RoleNames.ProjectOfficer);
        var activeOfficerIds = roleUsers
            .Where(IsActiveUser)
            .Select(user => user.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (activeOfficerIds.Length == 0)
        {
            return 0;
        }

        var projectOfficerIds = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active
                && project.LeadPoUserId != null
                && activeOfficerIds.Contains(project.LeadPoUserId))
            .Select(project => project.LeadPoUserId!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var ideaOfficerIds = await _db.ProjectIdeas
            .AsNoTracking()
            .Where(idea =>
                !idea.IsDeleted
                && idea.Status != ProjectIdeaStatuses.Archived
                && idea.AssignedProjectOfficerUserId != null
                && activeOfficerIds.Contains(idea.AssignedProjectOfficerUserId))
            .Select(idea => idea.AssignedProjectOfficerUserId!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var taskOfficerIds = await _db.ActionTasks
            .AsNoTracking()
            .Where(task =>
                !task.IsDeleted
                && task.Status != ActionTaskStatuses.Closed
                && task.Status != ActionTaskStatuses.Backlog
                && activeOfficerIds.Contains(task.AssignedToUserId))
            .Select(task => task.AssignedToUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return projectOfficerIds
            .Concat(ideaOfficerIds)
            .Concat(taskOfficerIds)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private async Task<IReadOnlyList<CommandOfficerWorkloadVm>> BuildCardsAsync(
        IReadOnlyCollection<ApplicationUser> users,
        CancellationToken cancellationToken)
    {
        var userIds = users
            .Select(user => user.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (userIds.Length == 0)
        {
            return Array.Empty<CommandOfficerWorkloadVm>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active
                && project.LeadPoUserId != null
                && userIds.Contains(project.LeadPoUserId))
            .Select(project => new ProjectRow(
                project.Id,
                project.Name,
                project.LeadPoUserId!,
                project.WorkflowVersion,
                project.ProjectStages
                    .Select(stage => new StageRow(
                        stage.StageCode,
                        stage.Status,
                        stage.SortOrder,
                        stage.ActualStart,
                        stage.CompletedOn))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var ideas = await _db.ProjectIdeas
            .AsNoTracking()
            .Where(idea =>
                !idea.IsDeleted
                && idea.Status != ProjectIdeaStatuses.Archived
                && idea.AssignedProjectOfficerUserId != null
                && userIds.Contains(idea.AssignedProjectOfficerUserId))
            .Select(idea => new IdeaRow(
                idea.Id,
                idea.Title,
                idea.Status,
                idea.AssignedProjectOfficerUserId!))
            .ToListAsync(cancellationToken);

        var tasks = await _db.ActionTasks
            .AsNoTracking()
            .Where(task =>
                !task.IsDeleted
                && task.Status != ActionTaskStatuses.Closed
                && task.Status != ActionTaskStatuses.Backlog
                && userIds.Contains(task.AssignedToUserId))
            .Select(task => new TaskRow(
                task.Id,
                task.Title,
                task.Status,
                task.DueDate,
                task.AssignedToUserId))
            .ToListAsync(cancellationToken);

        var projectsByOfficer = projects
            .GroupBy(project => project.OfficerUserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProjectRow>)group.ToList(),
                StringComparer.Ordinal);

        var ideasByOfficer = ideas
            .GroupBy(idea => idea.OfficerUserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IdeaRow>)group.ToList(),
                StringComparer.Ordinal);

        var tasksByOfficer = tasks
            .GroupBy(task => task.OfficerUserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TaskRow>)group.ToList(),
                StringComparer.Ordinal);

        var result = new List<CommandOfficerWorkloadVm>(users.Count);
        foreach (var user in users)
        {
            projectsByOfficer.TryGetValue(user.Id, out var officerProjects);
            ideasByOfficer.TryGetValue(user.Id, out var officerIdeas);
            tasksByOfficer.TryGetValue(user.Id, out var officerTasks);

            officerProjects ??= Array.Empty<ProjectRow>();
            officerIdeas ??= Array.Empty<IdeaRow>();
            officerTasks ??= Array.Empty<TaskRow>();

            if (officerProjects.Count + officerIdeas.Count + officerTasks.Count == 0)
            {
                continue;
            }

            var projectCards = officerProjects
                .Select(BuildProjectCard)
                .OrderBy(project => project.StageOrder)
                .ThenBy(project => project.ViewModel.Name, StringComparer.OrdinalIgnoreCase)
                .Select(project => project.ViewModel)
                .ToList();

            var ideaCards = officerIdeas
                .OrderByDescending(idea => idea.Id)
                .Select(idea => new CommandOfficerIdeaVm(
                    idea.Id,
                    idea.Title,
                    ProjectIdeaStatuses.ToDisplay(idea.Status),
                    $"/ProjectIdeas/Details/{idea.Id}"))
                .ToList();

            var taskCards = officerTasks
                .OrderBy(task => task.DueDate)
                .ThenBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
                .Select(task => new CommandOfficerTaskVm(
                    task.Id,
                    task.Title,
                    task.Status,
                    task.DueDate,
                    $"/ActionTasks/Index?taskId={task.Id}"))
                .ToList();

            result.Add(new CommandOfficerWorkloadVm
            {
                UserId = user.Id,
                OfficerName = string.IsNullOrWhiteSpace(user.FullName)
                    ? user.UserName ?? "Project Officer"
                    : user.FullName,
                Rank = user.Rank ?? string.Empty,
                ProjectCount = projectCards.Count,
                IdeaCount = ideaCards.Count,
                OtherTaskCount = taskCards.Count,
                Projects = projectCards,
                Ideas = ideaCards,
                OtherTasks = taskCards
            });
        }

        return result;
    }

    private ProjectCard BuildProjectCard(ProjectRow project)
    {
        var snapshots = project.Stages
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
            project.WorkflowVersion);

        var stageCode = string.IsNullOrWhiteSpace(presentStage.CurrentStageCode)
            ? UnassignedStageCode
            : presentStage.CurrentStageCode.Trim();
        var stageName = stageCode == UnassignedStageCode
            ? "Unassigned"
            : presentStage.CurrentStageName
                ?? _workflowStageMetadataProvider.GetDisplayName(project.WorkflowVersion, stageCode);
        var stageOrder = stageCode == UnassignedStageCode
            ? int.MaxValue
            : ProcurementWorkflow.OrderOf(project.WorkflowVersion, stageCode);

        return new ProjectCard(
            stageOrder,
            new CommandOfficerProjectVm(
                project.Id,
                project.Name,
                stageCode,
                stageName,
                $"/Projects/Overview/{project.Id}"));
    }

    private async Task<IReadOnlyList<CommandOfficerWorkloadVm>> ApplySavedOrderAsync(
        IReadOnlyList<CommandOfficerWorkloadVm> defaultOrder,
        string requestingUserId,
        CancellationToken cancellationToken)
    {
        if (defaultOrder.Count == 0 || string.IsNullOrWhiteSpace(requestingUserId))
        {
            return defaultOrder;
        }

        var orderJson = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == requestingUserId)
            .Select(user => user.ComdtOfficerWorkloadOrderJson)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(orderJson))
        {
            return defaultOrder;
        }

        try
        {
            var savedOrder = JsonSerializer.Deserialize<List<string>>(orderJson) ?? new List<string>();
            var positions = savedOrder
                .Select((id, index) => new { Id = id, Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Index,
                    StringComparer.Ordinal);

            return defaultOrder
                .OrderBy(card => positions.TryGetValue(card.UserId, out var position)
                    ? position
                    : int.MaxValue)
                .ThenBy(card => RankOrder(card.Rank))
                .ThenBy(card => card.OfficerName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return defaultOrder;
        }
    }

    private static bool IsActiveUser(ApplicationUser user)
        => !user.IsDisabled && !user.PendingDeletion;

    private static int RankOrder(string? rank)
    {
        if (string.IsNullOrWhiteSpace(rank))
        {
            return int.MaxValue;
        }

        var value = rank.Trim().ToUpperInvariant();
        if (value.Contains("LT COL", StringComparison.Ordinal) || value.Contains("LIEUTENANT COLONEL", StringComparison.Ordinal)) return 20;
        if (value.Contains("COLONEL", StringComparison.Ordinal) || value == "COL") return 10;
        if (value.Contains("MAJOR", StringComparison.Ordinal) || value == "MAJ") return 30;
        if (value.Contains("CAPTAIN", StringComparison.Ordinal) || value == "CAPT") return 40;
        if (value.Contains("LIEUTENANT", StringComparison.Ordinal) || value == "LT") return 50;
        return 100;
    }

    private sealed record ProjectRow(
        int Id,
        string Name,
        string OfficerUserId,
        string? WorkflowVersion,
        IReadOnlyList<StageRow> Stages);

    private sealed record StageRow(
        string StageCode,
        StageStatus Status,
        int SortOrder,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    private sealed record IdeaRow(int Id, string Title, string Status, string OfficerUserId);

    private sealed record TaskRow(int Id, string Title, string Status, DateTime DueDate, string OfficerUserId);

    private sealed record ProjectCard(int StageOrder, CommandOfficerProjectVm ViewModel);
}
