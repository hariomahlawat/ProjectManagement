using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class CommandWorkspaceService
{
    private const string UnassignedStageCode = "UNASSIGNED";
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public CommandWorkspaceService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<CommandWorkspaceVm> GetAsync(CommandWorkspaceQuery query, CancellationToken ct)
    {
        var categories = await _db.ProjectCategories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new CategoryRow(x.Id, x.Name, x.ParentId, x.SortOrder))
            .ToListAsync(ct);

        var parentOptions = categories.Where(x => x.ParentId == null)
            .Select(x => new CommandFilterOptionVm(x.Id, x.Name)).ToList();
        var selectedParents = query.ParentCategoryIds.Where(id => parentOptions.Any(x => x.Id == id)).Distinct().ToList();

        var projectRows = await _db.Projects.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Select(p => new ProjectRow(
                p.Id,
                p.Name,
                p.CategoryId,
                p.LeadPoUserId,
                p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                p.LeadPoUser != null ? p.LeadPoUser.Rank : null,
                p.ProjectStages.Select(s => new StageRow(s.StageCode, s.Status, s.SortOrder, s.CompletedOn)).ToList()))
            .ToListAsync(ct);

        var categoryMap = categories.ToDictionary(x => x.Id);
        var normalizedProjects = projectRows.Select(row => NormalizeProject(row, categoryMap)).ToList();
        var filteredProjects = normalizedProjects.AsEnumerable();
        if (selectedParents.Count > 0)
            filteredProjects = filteredProjects.Where(p => p.ParentCategoryId.HasValue && selectedParents.Contains(p.ParentCategoryId.Value));
        if (!string.IsNullOrWhiteSpace(query.ProjectSearch))
            filteredProjects = filteredProjects.Where(p => p.Name.Contains(query.ProjectSearch.Trim(), StringComparison.OrdinalIgnoreCase));
        var projectList = filteredProjects.ToList();

        var orderedStageCodes = StageCodes.All
            .Concat(projectList.Select(p => p.StageCode).Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stageColumns = BuildStageColumns(projectList, orderedStageCodes, query.PopulatedStagesOnly);
        var stageSeries = projectList
            .GroupBy(p => new { p.StageCode, p.StageName, p.ParentCategoryName })
            .Select(g => new CommandStageSeriesPointVm(g.Key.StageCode, g.Key.StageName, g.Key.ParentCategoryName, g.Count()))
            .OrderBy(x => StageOrder(x.StageCode, orderedStageCodes)).ThenBy(x => x.CategoryName)
            .ToList();

        var officers = await BuildOfficerWorkloadAsync(query.RequestingUserId, normalizedProjects, orderedStageCodes, ct);
        var stageOptions = orderedStageCodes
            .Select((code, index) => new CommandFilterOptionVm(index + 1, code == UnassignedStageCode ? "Unassigned" : StageCodes.DisplayNameOf(code)))
            .ToList();

        return new CommandWorkspaceVm
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ActiveView = query.View,
            TotalOngoingProjects = projectRows.Count,
            ParentCategoryOptions = parentOptions,
            SelectedParentCategoryIds = selectedParents,
            ProjectSearch = query.ProjectSearch,
            PopulatedStagesOnly = query.PopulatedStagesOnly,
            StageSeries = stageSeries,
            StageColumns = stageColumns,
            Officers = officers,
            StageOptions = stageOptions,
            ProjectOfficerCount = officers.Count
        };
    }

    /// <summary>
    /// Returns the exact workload card model used in the Comdt/HoD officer board for one officer.
    /// The Project Officer workspace uses this method so both audiences always see the same
    /// assignments, counts, ordering and stage resolution.
    /// </summary>
    public async Task<CommandOfficerWorkloadVm?> GetOfficerWorkloadCardAsync(
        string officerUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(officerUserId))
        {
            return null;
        }

        var projectRows = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted &&
                !project.IsArchived &&
                project.LifecycleStatus == ProjectLifecycleStatus.Active &&
                project.LeadPoUserId == officerUserId)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.LeadPoUserId,
                OfficerName = project.LeadPoUser != null ? project.LeadPoUser.FullName : null,
                OfficerRank = project.LeadPoUser != null ? project.LeadPoUser.Rank : null,
                Stages = project.ProjectStages
                    .Select(stage => new StageRow(
                        stage.StageCode,
                        stage.Status,
                        stage.SortOrder,
                        stage.CompletedOn))
                    .ToList()
            })
            .ToListAsync(ct);

        var normalizedProjects = projectRows
            .Select(project =>
            {
                var stage = DetermineCurrentStage(project.Stages);
                var stageCode = string.IsNullOrWhiteSpace(stage?.StageCode)
                    ? UnassignedStageCode
                    : stage!.StageCode.Trim();

                return new NormalizedProject(
                    project.Id,
                    project.Name,
                    null,
                    "Uncategorised",
                    stageCode,
                    stageCode == UnassignedStageCode ? "Unassigned" : StageCodes.DisplayNameOf(stageCode),
                    project.LeadPoUserId,
                    string.IsNullOrWhiteSpace(project.OfficerName)
                        ? "Unassigned"
                        : string.Join(' ', new[] { project.OfficerRank, project.OfficerName }
                            .Where(value => !string.IsNullOrWhiteSpace(value))));
            })
            .ToList();

        var orderedStageCodes = StageCodes.All
            .Concat(normalizedProjects
                .Select(project => project.StageCode)
                .Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cards = await BuildOfficerWorkloadAsync(
            string.Empty,
            normalizedProjects,
            orderedStageCodes,
            ct,
            onlyOfficerUserId: officerUserId,
            includeEmptyOfficer: true);

        return cards.SingleOrDefault();
    }

    private async Task<IReadOnlyList<CommandOfficerWorkloadVm>> BuildOfficerWorkloadAsync(
        string requestingUserId,
        IReadOnlyList<NormalizedProject> allProjects,
        IReadOnlyList<string> orderedStageCodes,
        CancellationToken ct,
        string? onlyOfficerUserId = null,
        bool includeEmptyOfficer = false)
    {
        var roleUsers = await _userManager.GetUsersInRoleAsync(RoleNames.ProjectOfficer);
        var users = roleUsers
            .Where(user =>
                !user.IsDisabled &&
                !user.PendingDeletion &&
                (string.IsNullOrWhiteSpace(onlyOfficerUserId) ||
                 string.Equals(user.Id, onlyOfficerUserId, StringComparison.Ordinal)))
            .ToList();
        var ids = users.Select(x => x.Id).ToList();

        var ideas = await _db.ProjectIdeas.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != ProjectIdeaStatuses.Archived && i.AssignedProjectOfficerUserId != null && ids.Contains(i.AssignedProjectOfficerUserId))
            .Select(i => new { i.Id, i.Title, i.Status, UserId = i.AssignedProjectOfficerUserId! })
            .ToListAsync(ct);

        var tasks = await _db.ActionTasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status != ActionTaskStatuses.Closed && t.Status != ActionTaskStatuses.Backlog && ids.Contains(t.AssignedToUserId))
            .Select(t => new { t.Id, t.Title, t.Status, t.DueDate, UserId = t.AssignedToUserId })
            .ToListAsync(ct);

        var result = new List<CommandOfficerWorkloadVm>();
        foreach (var user in users)
        {
            var officerProjects = allProjects.Where(p => string.Equals(p.LeadPoUserId, user.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            var officerIdeas = ideas.Where(i => i.UserId == user.Id).ToList();
            var officerTasks = tasks.Where(t => t.UserId == user.Id).ToList();
            if (!includeEmptyOfficer &&
                officerProjects.Count + officerIdeas.Count + officerTasks.Count == 0)
            {
                continue;
            }

            result.Add(new CommandOfficerWorkloadVm
            {
                UserId = user.Id,
                OfficerName = string.IsNullOrWhiteSpace(user.FullName) ? (user.UserName ?? "Project Officer") : user.FullName,
                Rank = user.Rank ?? string.Empty,
                ProjectCount = officerProjects.Count,
                IdeaCount = officerIdeas.Count,
                OtherTaskCount = officerTasks.Count,
                Projects = officerProjects.OrderBy(p => StageOrder(p.StageCode, orderedStageCodes)).ThenBy(p => p.Name)
                    .Select(p => new CommandOfficerProjectVm(p.Id, p.Name, p.StageCode, p.StageName, $"/Projects/Overview/{p.Id}" )).ToList(),
                Ideas = officerIdeas.OrderByDescending(i => i.Id)
                    .Select(i => new CommandOfficerIdeaVm(i.Id, i.Title, ProjectIdeaStatuses.ToDisplay(i.Status), $"/ProjectIdeas/Details/{i.Id}" )).ToList(),
                OtherTasks = officerTasks.OrderBy(t => t.DueDate)
                    .Select(t => new CommandOfficerTaskVm(t.Id, t.Title, t.Status, t.DueDate, $"/ActionTasks/Index?taskId={t.Id}" )).ToList()
            });
        }

        var defaultOrder = result
            .OrderBy(x => RankOrder(x.Rank))
            .ThenBy(x => x.OfficerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(requestingUserId)) return defaultOrder;
        var orderJson = await _db.Users.AsNoTracking()
            .Where(x => x.Id == requestingUserId)
            .Select(x => x.ComdtOfficerWorkloadOrderJson)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(orderJson)) return defaultOrder;

        try
        {
            var savedOrder = JsonSerializer.Deserialize<List<string>>(orderJson) ?? new List<string>();
            var positions = savedOrder
                .Select((id, index) => new { id, index })
                .Where(x => !string.IsNullOrWhiteSpace(x.id))
                .GroupBy(x => x.id, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First().index, StringComparer.Ordinal);
            return defaultOrder
                .OrderBy(x => positions.TryGetValue(x.UserId, out var position) ? position : int.MaxValue)
                .ThenBy(x => RankOrder(x.Rank))
                .ThenBy(x => x.OfficerName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return defaultOrder;
        }
    }

    private static int RankOrder(string? rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return int.MaxValue;
        var value = rank.Trim().ToUpperInvariant();
        if (value.Contains("LT COL") || value.Contains("LIEUTENANT COLONEL")) return 20;
        if (value.Contains("COLONEL") || value == "COL") return 10;
        if (value.Contains("MAJOR") || value == "MAJ") return 30;
        if (value.Contains("CAPTAIN") || value == "CAPT") return 40;
        if (value.Contains("LIEUTENANT") || value == "LT") return 50;
        return 100;
    }

    private static IReadOnlyList<CommandStageColumnVm> BuildStageColumns(IReadOnlyList<NormalizedProject> projects, IReadOnlyList<string> orderedCodes, bool populatedOnly)
    {
        var columns = orderedCodes.Select(code =>
        {
            var stageProjects = projects.Where(p => string.Equals(p.StageCode, code, StringComparison.OrdinalIgnoreCase)).ToList();
            return new CommandStageColumnVm
            {
                StageCode = code,
                StageName = code == UnassignedStageCode ? "Unassigned" : StageCodes.DisplayNameOf(code),
                ProjectCount = stageProjects.Count,
                Categories = stageProjects.GroupBy(p => p.ParentCategoryName)
                    .OrderBy(g => g.Key)
                    .Select(g => new CommandStageCategoryVm
                    {
                        CategoryName = g.Key,
                        ProjectCount = g.Count(),
                        Projects = g.OrderBy(p => p.Name).Select(p => new CommandStageProjectVm(p.Id, p.Name, p.OfficerDisplayName, $"/Projects/Overview/{p.Id}")).ToList()
                    }).ToList()
            };
        }).ToList();
        return populatedOnly ? columns.Where(x => x.ProjectCount > 0).ToList() : columns;
    }

    private static NormalizedProject NormalizeProject(ProjectRow row, IReadOnlyDictionary<int, CategoryRow> categories)
    {
        var stage = DetermineCurrentStage(row.Stages);
        var stageCode = string.IsNullOrWhiteSpace(stage?.StageCode) ? UnassignedStageCode : stage!.StageCode.Trim();
        int? parentId = null;
        var parentName = "Uncategorised";
        if (row.CategoryId.HasValue && categories.TryGetValue(row.CategoryId.Value, out var category))
        {
            var cursor = category;
            var guard = 0;
            while (cursor.ParentId.HasValue && categories.TryGetValue(cursor.ParentId.Value, out var parent) && guard++ < 20) cursor = parent;
            parentId = cursor.Id;
            parentName = cursor.Name;
        }
        return new NormalizedProject(row.Id, row.Name, parentId, parentName, stageCode,
            stageCode == UnassignedStageCode ? "Unassigned" : StageCodes.DisplayNameOf(stageCode), row.LeadPoUserId,
            string.IsNullOrWhiteSpace(row.OfficerName) ? "Unassigned" : string.Join(' ', new[] { row.OfficerRank, row.OfficerName }.Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private static StageRow? DetermineCurrentStage(IReadOnlyList<StageRow> stages)
    {
        if (stages.Count == 0) return null;
        return stages.Where(s => s.Status == StageStatus.InProgress).OrderBy(s => s.SortOrder).ThenBy(s => s.StageCode).FirstOrDefault()
            ?? stages.Where(s => s.Status == StageStatus.NotStarted).OrderBy(s => s.SortOrder).ThenBy(s => s.StageCode).FirstOrDefault()
            ?? stages.Where(s => s.Status == StageStatus.Completed).OrderByDescending(s => s.CompletedOn ?? DateOnly.MinValue).ThenByDescending(s => s.SortOrder).FirstOrDefault()
            ?? stages.OrderBy(s => s.SortOrder).FirstOrDefault();
    }

    private static string DisplayName(ApplicationUser user) => string.Join(' ', new[] { user.Rank, user.FullName }.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static int StageOrder(string code, IReadOnlyList<string> orderedCodes) { var i = orderedCodes.ToList().FindIndex(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }

    private sealed record CategoryRow(int Id, string Name, int? ParentId, int SortOrder);
    private sealed record StageRow(string StageCode, StageStatus Status, int SortOrder, DateOnly? CompletedOn);
    private sealed record ProjectRow(int Id, string Name, int? CategoryId, string? LeadPoUserId, string? OfficerName, string? OfficerRank, IReadOnlyList<StageRow> Stages);
    private sealed record NormalizedProject(int Id, string Name, int? ParentCategoryId, string ParentCategoryName, string StageCode, string StageName, string? LeadPoUserId, string OfficerDisplayName);
}

public sealed class CommandWorkspaceQuery
{
    public string View { get; init; } = "portfolio";
    public IReadOnlyList<int> ParentCategoryIds { get; init; } = Array.Empty<int>();
    public string? ProjectSearch { get; init; }
    public bool PopulatedStagesOnly { get; init; }
    public string RequestingUserId { get; init; } = string.Empty;
}
