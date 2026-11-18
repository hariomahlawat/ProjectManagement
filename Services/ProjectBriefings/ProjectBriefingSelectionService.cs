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
    // Everything the Add projects filter offers, with counts: asked for when the dialog opens.
    Task<ProjectBriefingSelectionOptionsVm> GetOptionsAsync(CancellationToken cancellationToken = default);

    // The category lists alone (no counts), for the page itself: two small reads.
    Task<ProjectBriefingSelectionOptionsVm> GetCategoryOptionsAsync(CancellationToken cancellationToken = default);

    Task<ProjectBriefingSelectionResult> ResolveAsync(
        ProjectBriefingSelectionRequest request,
        CancellationToken cancellationToken = default);

    // The projects matching the Add projects filter (docs/ux/briefing-add-projects.md), by name.
    Task<IReadOnlyList<ProjectBriefingSearchResultVm>> FilterAsync(
        ProjectBriefingProjectFilter filter,
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

        var lifecycleCounts = await baseQuery
            .GroupBy(project => project.LifecycleStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
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

        var projectCategories = BuildCategoryOptions(await ProjectCategoryRowsAsync(cancellationToken), categoryCounts);
        var technicalCategories = BuildCategoryOptions(await TechnicalCategoryRowsAsync(cancellationToken), technicalCounts);

        var proliferationCount = await baseQuery
            .CountAsync(project => _db.ProjectTechStatuses.Any(status =>
                status.ProjectId == project.Id && status.AvailableForProliferation == true), cancellationToken);

        // Counted over the projects the filter can return (Codex review): a listed project since
        // archived, deleted or cancelled is not counted.
        var eligibleIds = (await baseQuery.Select(project => project.Id).ToListAsync(cancellationToken)).ToHashSet();
        var arppYears = (await ArppListingsAsync(Array.Empty<int>(), cancellationToken))
            .Where(listing => eligibleIds.Contains(listing.ProjectId))
            .GroupBy(listing => listing.FinancialYearStart)
            .OrderByDescending(group => group.Key)
            .Select(group => new ProjectBriefingArppYearOptionVm(
                group.Key,
                FinancialYearLabel(group.Key),
                group.Select(listing => listing.ProjectId).Distinct().Count()))
            .ToArray();
        var completionFinancialYears = (await CompletionsAsync(cancellationToken))
            .Select(completion => completion.FinancialYearStart)
            .ToArray();
        var thisFinancialYear = FinancialYearOf(DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

        return new ProjectBriefingSelectionOptionsVm
        {
            OngoingCount = lifecycleCounts.FirstOrDefault(row => row.Status == ProjectLifecycleStatus.Active)?.Count ?? 0,
            CompletedCount = lifecycleCounts.FirstOrDefault(row => row.Status == ProjectLifecycleStatus.Completed)?.Count ?? 0,
            ProliferationAvailableCount = proliferationCount,
            ProjectCategories = projectCategories,
            TechnicalCategories = technicalCategories,
            ArppYears = arppYears,
            MinimumCompletionFinancialYear = completionFinancialYears.Length == 0 ? thisFinancialYear : completionFinancialYears.Min(),
            MaximumCompletionFinancialYear = completionFinancialYears.Length == 0 ? thisFinancialYear : completionFinancialYears.Max()
        };
    }

    public async Task<ProjectBriefingSelectionOptionsVm> GetCategoryOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var none = new Dictionary<int, int>();
        return new ProjectBriefingSelectionOptionsVm
        {
            ProjectCategories = BuildCategoryOptions(await ProjectCategoryRowsAsync(cancellationToken), none),
            TechnicalCategories = BuildCategoryOptions(await TechnicalCategoryRowsAsync(cancellationToken), none)
        };
    }

    private Task<List<CategoryOptionRow>> ProjectCategoryRowsAsync(CancellationToken cancellationToken)
        => _db.ProjectCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryOptionRow(category.Id, category.Name, category.ParentId))
            .ToListAsync(cancellationToken);

    private Task<List<CategoryOptionRow>> TechnicalCategoryRowsAsync(CancellationToken cancellationToken)
        => _db.TechnicalCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryOptionRow(category.Id, category.Name, category.ParentId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectBriefingSearchResultVm>> FilterAsync(
        ProjectBriefingProjectFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = EligibleProjects();

        var text = filter.Query?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            query = WhereMatches(query, text);
        }

        var years = filter.ArppFinancialYears.Where(year => year > 0).Distinct().ToArray();
        if (years.Length > 0)
        {
            var listed = (await ArppListingsAsync(years, cancellationToken))
                .Select(listing => listing.ProjectId)
                .Distinct()
                .ToArray();
            query = query.Where(project => listed.Contains(project.Id));
        }

        var projectCategories = filter.ProjectCategoryIds.Where(id => id > 0).Distinct().ToArray();
        if (projectCategories.Length > 0)
        {
            var expanded = await ExpandCategoryIdsAsync(projectCategories, technical: false, cancellationToken);
            query = query.Where(project => project.CategoryId.HasValue && expanded.Contains(project.CategoryId.Value));
        }

        var technicalCategories = filter.TechnicalCategoryIds.Where(id => id > 0).Distinct().ToArray();
        if (technicalCategories.Length > 0)
        {
            var expanded = await ExpandCategoryIdsAsync(technicalCategories, technical: true, cancellationToken);
            query = query.Where(project => project.TechnicalCategoryId.HasValue && expanded.Contains(project.TechnicalCategoryId.Value));
        }

        var statuses = filter.Statuses
            .Where(status => status is ProjectLifecycleStatus.Active or ProjectLifecycleStatus.Completed)
            .Distinct()
            .ToArray();
        if (statuses.Length > 0)
        {
            query = query.Where(project => statuses.Contains(project.LifecycleStatus));
        }

        if (filter.CompletedFromFinancialYear.HasValue || filter.CompletedToFinancialYear.HasValue)
        {
            var from = filter.CompletedFromFinancialYear ?? int.MinValue;
            var to = filter.CompletedToFinancialYear ?? int.MaxValue;
            if (from > to)
            {
                (from, to) = (to, from);
            }

            var completed = (await CompletionsAsync(cancellationToken))
                .Where(completion => completion.FinancialYearStart >= from && completion.FinancialYearStart <= to)
                .Select(completion => completion.ProjectId)
                .ToArray();
            query = query.Where(project => completed.Contains(project.Id));
        }

        if (filter.AvailableForProliferation)
        {
            query = query.Where(project => _db.ProjectTechStatuses.Any(status =>
                status.ProjectId == project.Id && status.AvailableForProliferation == true));
        }

        // Every match, with no limit (Codex review): the dialog lists them all and its totals are
        // the real ones.
        return await BuildResultsAsync(query, take: null, cancellationToken);
    }

    // The projects each published ARPP of these financial years lists (all years when none are
    // given): a project is listed in a year when it appears in any issue of that year (the main
    // issue or an addendum) and its latest entry in that year is not Delisted.
    private async Task<IReadOnlyList<ArppListing>> ArppListingsAsync(
        IReadOnlyCollection<int> financialYears,
        CancellationToken cancellationToken)
    {
        var entries = _db.ArppPublishedEntries.AsNoTracking().Where(entry => entry.ProjectId.HasValue);
        if (financialYears.Count > 0)
        {
            entries = entries.Where(entry => financialYears.Contains(entry.PublishedIssue.FinancialYearStart));
        }

        var rows = await entries
            .Select(entry => new
            {
                ProjectId = entry.ProjectId!.Value,
                entry.Id,
                entry.Category,
                entry.PublishedIssue.FinancialYearStart,
                entry.PublishedIssue.IssueDate,
                entry.PublishedIssue.IssueSequence,
                entry.PublishedIssue.ArppIssueId
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.ProjectId, row.FinancialYearStart))
            .Select(group => group
                .OrderByDescending(row => row.IssueDate)
                .ThenByDescending(row => row.IssueSequence)
                .ThenByDescending(row => row.ArppIssueId)
                .ThenByDescending(row => row.Id)
                .First())
            .Where(latest => latest.Category != Models.Arpp.ArppCategory.Delisted)
            .Select(latest => new ArppListing(latest.ProjectId, latest.FinancialYearStart))
            .ToArray();
    }

    // Each completed project's financial year of completion: from its completion date, else its
    // recorded month and year; a year recorded without a month counts as the financial year that
    // starts in it.
    private async Task<IReadOnlyList<Completion>> CompletionsAsync(CancellationToken cancellationToken)
    {
        var rows = await EligibleProjects()
            .Where(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(project => new { project.Id, project.CompletedOn, project.CompletedYear, project.CompletedMonth })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => row.CompletedOn.HasValue
                ? new Completion(row.Id, FinancialYearOf(row.CompletedOn.Value))
                : row.CompletedYear.HasValue
                    ? new Completion(row.Id, row.CompletedMonth is >= 1 and <= 3 ? row.CompletedYear.Value - 1 : row.CompletedYear.Value)
                    : null)
            .Where(completion => completion is not null)
            .Select(completion => completion!)
            .ToArray();
    }

    // The financial year (April to March) a date falls in, by its starting year.
    internal static int FinancialYearOf(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;

    internal static string FinancialYearLabel(int startYear) => $"FY {startYear}-{(startYear + 1) % 100:00}";
    public async Task<ProjectBriefingSelectionResult> ResolveAsync(
        ProjectBriefingSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = EligibleProjects();
        string summary;

        // Add projects sends the ticked projects (docs/ux/briefing-add-projects.md). The earlier ways
        // (all ongoing, recently completed by calendar year, categories, proliferation, My projects)
        // went with their tabs; a request for one of them is refused (cold review, L1).
        if (request.Kind != ProjectBriefingSelectionKind.IndividualProjects)
        {
            throw new InvalidOperationException("This way of adding projects is no longer available. Tick the projects in the list instead.");
        }

        var ids = request.ProjectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            throw new InvalidOperationException("Select at least one project.");
        }

        query = query.Where(project => ids.Contains(project.Id));
        summary = "Individually selected projects";

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

    private static IQueryable<Project> WhereMatches(IQueryable<Project> query, string text)
    {
        var pattern = $"%{text}%";
        return query.Where(project =>
            EF.Functions.ILike(project.Name, pattern)
            || (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern))
            || (project.Category != null && EF.Functions.ILike(project.Category.Name, pattern))
            || (project.TechnicalCategory != null && EF.Functions.ILike(project.TechnicalCategory.Name, pattern))
            || (project.LeadPoUser != null
                && (EF.Functions.ILike(project.LeadPoUser.FullName, pattern)
                    || EF.Functions.ILike(project.LeadPoUser.Rank, pattern))));
    }

    // The result rows (stage, categories, officer) for the projects a query selects, by name.
    private async Task<IReadOnlyList<ProjectBriefingSearchResultVm>> BuildResultsAsync(
        IQueryable<Project> query,
        int? take,
        CancellationToken cancellationToken)
    {
        IQueryable<Project> ordered = query.OrderBy(project => project.Name);
        var projectRows = await (take.HasValue ? ordered.Take(take.Value) : ordered)
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

        // Depth-first: SortOrder is numbered within each parent, so a flat sort mixed one parent's
        // sub-categories in among the other parents (cold review, M3). A row whose parent is not
        // listed (inactive) stands at the top level.
        var listed = rows.Select(row => row.Id).ToHashSet();
        var ordered = new List<ProjectBriefingLookupOptionVm>(rows.Count);
        var placed = new HashSet<int>();
        void Place(CategoryOptionRow row, int depth)
        {
            if (!placed.Add(row.Id))
            {
                return;
            }

            ordered.Add(new ProjectBriefingLookupOptionVm(row.Id, row.Name, Aggregate(row.Id, new HashSet<int>()), row.ParentId, depth));
            foreach (var child in rows.Where(candidate => candidate.ParentId == row.Id))
            {
                Place(child, depth + 1);
            }
        }

        foreach (var row in rows.Where(row => !row.ParentId.HasValue || !listed.Contains(row.ParentId.Value)))
        {
            Place(row, 0);
        }

        // Anything left (a loop in the parents) still appears, at the top level.
        foreach (var row in rows)
        {
            Place(row, 0);
        }

        return ordered;
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
    private sealed record ArppListing(int ProjectId, int FinancialYearStart);
    private sealed record Completion(int ProjectId, int FinancialYearStart);
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
