using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingSelectionService
{
    Task<ProjectBriefingSelectionOptionsVm> GetOptionsAsync(CancellationToken cancellationToken = default);

    Task<ProjectBriefingSelectionResult> ResolveAsync(
        ProjectBriefingSelectionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectBriefingSearchResultVm>> SearchAsync(
        string? query,
        int take = 25,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> ValidateProjectIdsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingSelectionService : IProjectBriefingSelectionService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProjectBriefingSelectionService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectBriefingSelectionOptionsVm> GetOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var baseQuery = EligibleProjects();
        var todayYear = _clock.UtcNow.Year;

        var lifecycleCounts = await baseQuery
            .GroupBy(project => project.LifecycleStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var completionYears = await baseQuery
            .Where(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(project => project.CompletedYear ?? (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : (int?)null))
            .Where(year => year.HasValue)
            .Select(year => year!.Value)
            .ToListAsync(cancellationToken);

        var categoryCounts = await baseQuery
            .Where(project => project.CategoryId.HasValue)
            .GroupBy(project => project.CategoryId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Id, row => row.Count, cancellationToken);

        var technicalCounts = await baseQuery
            .Where(project => project.TechnicalCategoryId.HasValue)
            .GroupBy(project => project.TechnicalCategoryId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Id, row => row.Count, cancellationToken);

        var projectCategoryRows = await _db.ProjectCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryOptionRow(category.Id, category.Name, category.ParentId))
            .ToListAsync(cancellationToken);
        var projectCategories = BuildCategoryOptions(projectCategoryRows, categoryCounts);

        var technicalCategoryRows = await _db.TechnicalCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryOptionRow(category.Id, category.Name, category.ParentId))
            .ToListAsync(cancellationToken);
        var technicalCategories = BuildCategoryOptions(technicalCategoryRows, technicalCounts);

        var proliferationCount = await baseQuery
            .CountAsync(project => _db.ProjectTechStatuses.Any(status =>
                status.ProjectId == project.Id && status.AvailableForProliferation), cancellationToken);

        return new ProjectBriefingSelectionOptionsVm
        {
            OngoingCount = lifecycleCounts.FirstOrDefault(row => row.Status == ProjectLifecycleStatus.Active)?.Count ?? 0,
            CompletedCount = lifecycleCounts.FirstOrDefault(row => row.Status == ProjectLifecycleStatus.Completed)?.Count ?? 0,
            ProliferationAvailableCount = proliferationCount,
            MinimumCompletionYear = completionYears.Count == 0 ? todayYear : completionYears.Min(),
            MaximumCompletionYear = completionYears.Count == 0 ? todayYear : completionYears.Max(),
            ProjectCategories = projectCategories,
            TechnicalCategories = technicalCategories
        };
    }

    public async Task<ProjectBriefingSelectionResult> ResolveAsync(
        ProjectBriefingSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = EligibleProjects();
        string summary;

        switch (request.Kind)
        {
            case ProjectBriefingSelectionKind.Ongoing:
                query = query.Where(project => project.LifecycleStatus == ProjectLifecycleStatus.Active);
                summary = "All ongoing projects";
                break;

            case ProjectBriefingSelectionKind.RecentlyCompleted:
            {
                var currentYear = _clock.UtcNow.Year;
                var from = request.CompletionYearFrom ?? currentYear - 1;
                var to = request.CompletionYearTo ?? currentYear;
                if (from > to)
                {
                    (from, to) = (to, from);
                }

                query = query.Where(project =>
                    project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    && (project.CompletedYear ?? (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : 0)) >= from
                    && (project.CompletedYear ?? (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : 0)) <= to);
                summary = from == to
                    ? $"Projects completed in {from}"
                    : $"Projects completed from {from} to {to}";
                break;
            }

            case ProjectBriefingSelectionKind.ProjectCategory:
            {
                var selected = request.ProjectCategoryIds.Where(id => id > 0).Distinct().ToArray();
                if (selected.Length == 0)
                {
                    throw new InvalidOperationException("Select at least one project category.");
                }

                var expanded = await ExpandCategoryIdsAsync(selected, technical: false, cancellationToken);
                query = query.Where(project => project.CategoryId.HasValue && expanded.Contains(project.CategoryId.Value));
                summary = "Selected project categories";
                break;
            }

            case ProjectBriefingSelectionKind.TechnicalCategory:
            {
                var selected = request.TechnicalCategoryIds.Where(id => id > 0).Distinct().ToArray();
                if (selected.Length == 0)
                {
                    throw new InvalidOperationException("Select at least one technical category.");
                }

                var expanded = await ExpandCategoryIdsAsync(selected, technical: true, cancellationToken);
                query = query.Where(project => project.TechnicalCategoryId.HasValue && expanded.Contains(project.TechnicalCategoryId.Value));
                summary = "Selected technical categories";
                break;
            }

            case ProjectBriefingSelectionKind.AvailableForProliferation:
                query = query.Where(project => _db.ProjectTechStatuses.Any(status =>
                    status.ProjectId == project.Id && status.AvailableForProliferation));
                summary = "Projects available for proliferation";
                break;

            case ProjectBriefingSelectionKind.IndividualProjects:
            {
                var ids = request.ProjectIds.Where(id => id > 0).Distinct().ToArray();
                if (ids.Length == 0)
                {
                    throw new InvalidOperationException("Select at least one project.");
                }
                query = query.Where(project => ids.Contains(project.Id));
                summary = "Individually selected projects";
                break;
            }

            default:
                throw new InvalidOperationException("The project-selection method is not supported.");
        }

        var projectIds = await query
            .OrderBy(project => project.Name)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);

        var rulesJson = JsonSerializer.Serialize(new
        {
            kind = request.Kind.ToString(),
            projectCategoryIds = request.ProjectCategoryIds,
            technicalCategoryIds = request.TechnicalCategoryIds,
            projectIds = request.ProjectIds,
            completionYearFrom = request.CompletionYearFrom,
            completionYearTo = request.CompletionYearTo
        });

        return new ProjectBriefingSelectionResult(projectIds, summary, rulesJson);
    }

    public async Task<IReadOnlyList<ProjectBriefingSearchResultVm>> SearchAsync(
        string? query,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2)
        {
            return Array.Empty<ProjectBriefingSearchResultVm>();
        }

        take = Math.Clamp(take, 1, 50);
        var pattern = $"%{normalized}%";
        var projectRows = await EligibleProjects()
            .Where(project =>
                EF.Functions.ILike(project.Name, pattern)
                || (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern))
                || (project.Category != null && EF.Functions.ILike(project.Category.Name, pattern))
                || (project.TechnicalCategory != null && EF.Functions.ILike(project.TechnicalCategory.Name, pattern))
                || (project.LeadPoUser != null
                    && (EF.Functions.ILike(project.LeadPoUser.FullName, pattern)
                        || EF.Functions.ILike(project.LeadPoUser.Rank, pattern))))
            .OrderBy(project => project.Name)
            .Take(take)
            .Select(project => new SearchProjectBaseRow(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.LifecycleStatus,
                project.WorkflowVersion,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.LeadPoUser != null ? project.LeadPoUser.Rank : null,
                project.LeadPoUser != null ? project.LeadPoUser.FullName : null))
            .ToListAsync(cancellationToken);

        var projectIds = projectRows.Select(row => row.ProjectId).ToArray();
        var stageRows = projectIds.Length == 0
            ? new List<SearchStageDatabaseRow>()
            : await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => projectIds.Contains(stage.ProjectId))
                .Select(stage => new SearchStageDatabaseRow(
                    stage.ProjectId,
                    stage.StageCode,
                    stage.Status,
                    stage.SortOrder))
                .ToListAsync(cancellationToken);
        var stagesByProject = stageRows
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SearchStageRow>)group
                    .Select(stage => new SearchStageRow(stage.StageCode, stage.Status, stage.SortOrder))
                    .ToList());

        var rows = projectRows.Select(row => new SearchProjectRow(
            row.ProjectId,
            row.ProjectName,
            row.CaseFileNumber,
            row.LifecycleStatus,
            row.WorkflowVersion,
            row.ProjectCategory,
            row.TechnicalCategory,
            BuildOfficerDisplay(row.ProjectOfficerRank, row.ProjectOfficerName),
            stagesByProject.GetValueOrDefault(row.ProjectId) ?? Array.Empty<SearchStageRow>()))
            .ToList();

        return rows.Select(row => new ProjectBriefingSearchResultVm(
                row.ProjectId,
                row.ProjectName,
                LifecycleDisplay(row.LifecycleStatus),
                ResolveStage(row),
                row.ProjectCategory,
                row.TechnicalCategory,
                row.ProjectOfficer,
                row.CaseFileNumber))
            .ToList();
    }

    public async Task<IReadOnlyList<int>> ValidateProjectIdsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<int>();
        }

        return await EligibleProjects()
            .Where(project => ids.Contains(project.Id))
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Project> EligibleProjects()
        => _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus != ProjectLifecycleStatus.Cancelled);

    private async Task<int[]> ExpandCategoryIdsAsync(
        IReadOnlyCollection<int> selectedIds,
        bool technical,
        CancellationToken cancellationToken)
    {
        var rows = technical
            ? await _db.TechnicalCategories.AsNoTracking()
                .Select(category => new CategoryNode(category.Id, category.ParentId))
                .ToListAsync(cancellationToken)
            : await _db.ProjectCategories.AsNoTracking()
                .Select(category => new CategoryNode(category.Id, category.ParentId))
                .ToListAsync(cancellationToken);

        var children = rows
            .Where(row => row.ParentId.HasValue)
            .GroupBy(row => row.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Id).ToArray());

        var resolved = new HashSet<int>(selectedIds);
        var pending = new Queue<int>(selectedIds);
        while (pending.TryDequeue(out var parentId))
        {
            if (!children.TryGetValue(parentId, out var childIds))
            {
                continue;
            }

            foreach (var childId in childIds)
            {
                if (resolved.Add(childId))
                {
                    pending.Enqueue(childId);
                }
            }
        }

        return resolved.ToArray();
    }

    private static IReadOnlyList<ProjectBriefingLookupOptionVm> BuildCategoryOptions(
        IReadOnlyList<CategoryOptionRow> rows,
        IReadOnlyDictionary<int, int> directCounts)
    {
        var children = rows
            .Where(row => row.ParentId.HasValue)
            .GroupBy(row => row.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Id).ToArray());

        int Aggregate(int categoryId, HashSet<int> visiting)
        {
            if (!visiting.Add(categoryId))
            {
                return directCounts.GetValueOrDefault(categoryId);
            }

            var count = directCounts.GetValueOrDefault(categoryId);
            if (children.TryGetValue(categoryId, out var childIds))
            {
                foreach (var childId in childIds)
                {
                    count += Aggregate(childId, visiting);
                }
            }
            visiting.Remove(categoryId);
            return count;
        }

        return rows
            .Select(row => new ProjectBriefingLookupOptionVm(
                row.Id,
                row.Name,
                Aggregate(row.Id, new HashSet<int>()),
                row.ParentId))
            .ToList();
    }

    private static string? BuildOfficerDisplay(string? rank, string? name)
    {
        var parts = new[] { rank?.Trim(), name?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var display = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(display) ? null : display;
    }

    private static string ResolveStage(SearchProjectRow project)
    {
        if (project.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return "Completed";
        }

        var stageCodes = ProcurementWorkflow.StageCodesFor(project.WorkflowVersion);
        var statusByCode = project.Stages
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(stage => stage.SortOrder).First().Status,
                StringComparer.OrdinalIgnoreCase);

        var statuses = stageCodes
            .Select(code => statusByCode.GetValueOrDefault(code, StageStatus.NotStarted))
            .ToArray();
        var index = OngoingStagePresentationPolicy.ResolveCurrentStageIndex(statuses);
        return StageCodes.DisplayNameOf(project.WorkflowVersion, stageCodes[index]);
    }

    private static string LifecycleDisplay(ProjectLifecycleStatus status)
        => status == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing";

    private sealed record CategoryNode(int Id, int? ParentId);
    private sealed record CategoryOptionRow(int Id, string Name, int? ParentId);

    private sealed record SearchProjectBaseRow(
        int ProjectId,
        string ProjectName,
        string? CaseFileNumber,
        ProjectLifecycleStatus LifecycleStatus,
        string WorkflowVersion,
        string? ProjectCategory,
        string? TechnicalCategory,
        string? ProjectOfficerRank,
        string? ProjectOfficerName);

    private sealed record SearchProjectRow(
        int ProjectId,
        string ProjectName,
        string? CaseFileNumber,
        ProjectLifecycleStatus LifecycleStatus,
        string WorkflowVersion,
        string? ProjectCategory,
        string? TechnicalCategory,
        string? ProjectOfficer,
        IReadOnlyList<SearchStageRow> Stages);

    private sealed record SearchStageDatabaseRow(
        int ProjectId,
        string StageCode,
        StageStatus Status,
        int SortOrder);

    private sealed record SearchStageRow(string StageCode, StageStatus Status, int SortOrder);
}
