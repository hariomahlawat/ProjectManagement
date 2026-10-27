using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class IndexModel : PageModel
    {
        // Section: Paging defaults
        private const int DefaultPageSize = 25;
        private const int MaxPageSize = 100;
        private const int AllPageSizeValue = 0;

        private readonly ApplicationDbContext _db;
        private readonly IProjectAnalyticsService _analytics;
        private readonly ProjectCategoryHierarchyService _categoryHierarchy;
        private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
        private readonly ProjectRegisterFactsService _registerFacts;
        private readonly IProjectRegisterExcelBuilder _excelBuilder;
        private readonly IClock _clock;

        public IndexModel(
            ApplicationDbContext db,
            IProjectAnalyticsService analytics,
            ProjectCategoryHierarchyService categoryHierarchy,
            IWorkflowStageMetadataProvider workflowStageMetadataProvider,
            ProjectRegisterFactsService registerFacts,
            IProjectRegisterExcelBuilder excelBuilder,
            IClock clock)
        {
            _db = db;
            _analytics = analytics;
            _categoryHierarchy = categoryHierarchy;
            _workflowStageMetadataProvider = workflowStageMetadataProvider;
            _registerFacts = registerFacts;
            _excelBuilder = excelBuilder;
            _clock = clock;
        }

        public IReadOnlyList<Project> Projects { get; private set; } = new List<Project>();

        public IReadOnlyDictionary<int, ProjectRepositoryStagePositionVm> StagePositions { get; private set; }
            = new Dictionary<int, ProjectRepositoryStagePositionVm>();

        // Section: Active register (docs/ux/projects-register.md). The Active tab's table shows
        // each project's register facts; a project without facts (archived) shows without timing.
        public bool IsActiveRegister => Lifecycle == ProjectLifecycleFilter.Active;

        public IReadOnlyDictionary<int, ProjectRegisterActiveFacts> ActiveRegisterFacts { get; private set; }
            = new Dictionary<int, ProjectRegisterActiveFacts>();

        public IReadOnlyDictionary<int, ProjectRegisterActiveRowVm> ActiveRegisterRows { get; private set; }
            = new Dictionary<int, ProjectRegisterActiveRowVm>();

        // Active-tab filters from the pipeline strip and its quick filters. CurrentStage is the stage
        // a project is in now (StageCode, kept for older links, means "has reached").
        [BindProperty(SupportsGet = true)]
        public string? CurrentStage { get; set; }

        [BindProperty(SupportsGet = true)]
        public ProjectRegisterAttention? Attention { get; set; }

        public ProjectRegisterPipeline? Pipeline { get; private set; }

        // Completed tab: all completed, recorded in PRISM, or legacy entries. Lifecycle=Legacy (older
        // links) opens Completed with the legacy entries selected.
        [BindProperty(SupportsGet = true)]
        public ProjectRegisterRecord? Record { get; set; }

        public bool IsCompletedRegister => Lifecycle == ProjectLifecycleFilter.Completed;

        public bool IsCancelledRegister => Lifecycle == ProjectLifecycleFilter.Cancelled;

        public int RecordPrismCount { get; private set; }

        public int RecordLegacyCount { get; private set; }

        public IDictionary<string, string?> BuildRecordRoute(ProjectRegisterRecord? record)
        {
            var values = BuildBaseRouteValues();
            values["Record"] = record?.ToString();
            values["p"] = "1";
            return values;
        }

        public IReadOnlyDictionary<int, ProjectRegisterClosedRowVm> ClosedRegisterRows { get; private set; }
            = new Dictionary<int, ProjectRegisterClosedRowVm>();

        // Section: Quick-filter bar (docs/ux/projects-register.md)
        public IReadOnlyList<ProjectRegisterFacet> QuickFacets { get; private set; } = Array.Empty<ProjectRegisterFacet>();

        public IReadOnlyList<ProjectRegisterFilterChip> FilterChips { get; private set; } = Array.Empty<ProjectRegisterFilterChip>();

        // The rarer filters kept in the More filters drawer.
        public int MoreFiltersCount =>
            (string.IsNullOrWhiteSpace(HodUserId) ? 0 : 1)
            + (ProjectTypeId.HasValue || ProjectTypeUnclassified ? 1 : 0)
            + (CompletedYear.HasValue ? 1 : 0)
            + (TotStatus.HasValue ? 1 : 0)
            + (IncludeArchived ? 1 : 0);

        // Route values for a quick-filter option: sets that one filter, keeps the rest, back to page 1.
        public IDictionary<string, string?> BuildFacetRoute(string key, string? value)
        {
            var values = BuildBaseRouteValues();
            values[key] = value;
            if (key == "CategoryId")
            {
                values.Remove("IncludeCategoryDescendants");
            }

            values["p"] = "1";
            return values;
        }

        public IDictionary<string, string?> BuildClearRoute()
            => new Dictionary<string, string?>
            {
                ["Lifecycle"] = Lifecycle.ToString()
            };

        public string? CurrentStageFilterLabel => string.IsNullOrWhiteSpace(CurrentStage)
            ? null
            : $"Now in {_workflowStageMetadataProvider.GetDisplayName(null, CurrentStage)}";

        public string? AttentionFilterLabel => Attention?.Label();

        // Blocked first, then most overdue, unless a search or an explicit sort orders the list.
        private bool UsesAttentionOrder =>
            IsActiveRegister && Sort == ProjectRepositorySort.Operational && string.IsNullOrWhiteSpace(Query);

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CategoryId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? TechnicalCategoryId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? LeadPoUserId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? HodUserId { get; set; }

        [BindProperty(SupportsGet = true)]
        public ProjectLifecycleFilter Lifecycle { get; set; } = ProjectLifecycleFilter.All;

        [BindProperty(SupportsGet = true)]
        public int? CompletedYear { get; set; }

        [BindProperty(SupportsGet = true)]
        public ProjectTotStatus? TotStatus { get; set; }

        [BindProperty(SupportsGet = true, Name = "p")]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 25;

        [BindProperty(SupportsGet = true)]
        public bool IncludeArchived { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? StageCode { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? StageCompletedMonth { get; set; }

        // Date windows (inclusive, yyyy-MM-dd), set by Command desk drill-downs: projects completed
        // in the window, or projects with a stage completed in the window.
        [BindProperty(SupportsGet = true)]
        public DateOnly? CompletedFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? CompletedTo { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? StageCompletedFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? StageCompletedTo { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SlipBucket { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool IncludeCategoryDescendants { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? ProjectTypeId { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool ProjectTypeUnclassified { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Build { get; set; }

        [BindProperty(SupportsGet = true)]
        public ProjectRepositorySort Sort { get; set; } = ProjectRepositorySort.Operational;

        [BindProperty(SupportsGet = true)]
        public ProjectSortDirection Dir { get; set; } = ProjectSortDirection.Asc;

        public int TotalCount { get; private set; }

        // Section: KPI counters (filtered dataset)
        public int FilteredTotal { get; private set; }

        public int RepeatBuildCount { get; private set; }

        public int NewBuildCount { get; private set; }

        public IReadOnlyList<ProjectTypeChipViewModel> ProjectTypeChips { get; private set; }
            = Array.Empty<ProjectTypeChipViewModel>();

        public IReadOnlyDictionary<int, int> ProjectTypeCounts { get; private set; }
            = new Dictionary<int, int>();

        public int ProjectTypeUnclassifiedCount { get; private set; }

        public int TotalPages { get; private set; }

        public int ResultsStart { get; private set; }

        public int ResultsEnd { get; private set; }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(Query) ||
            CategoryId.HasValue ||
            TechnicalCategoryId.HasValue ||
            !string.IsNullOrWhiteSpace(LeadPoUserId) ||
            !string.IsNullOrWhiteSpace(HodUserId) ||
            Lifecycle != ProjectLifecycleFilter.All ||
            CompletedYear.HasValue ||
            TotStatus.HasValue ||
            IncludeArchived ||
            !string.IsNullOrWhiteSpace(StageCode) ||
            !string.IsNullOrWhiteSpace(StageCompletedMonth) ||
            CompletedFrom.HasValue ||
            CompletedTo.HasValue ||
            StageCompletedFrom.HasValue ||
            StageCompletedTo.HasValue ||
            !string.IsNullOrWhiteSpace(SlipBucket) ||
            ProjectTypeId.HasValue ||
            ProjectTypeUnclassified ||
            !string.IsNullOrWhiteSpace(Build) ||
            !string.IsNullOrWhiteSpace(CurrentStage) ||
            Attention.HasValue;

        public IEnumerable<SelectListItem> CategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> LeadPoOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> HodOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> CompletionYearOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> TotStatusOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IReadOnlyList<LifecycleFilterTab> LifecycleTabs { get; private set; } = Array.Empty<LifecycleFilterTab>();

        // Section: Result view preference (cards or table), written by projects-index.js so the
        // server renders the chosen view on first paint.
        public const string ViewCookieName = "prism_projects_view";

        // Section: Default tab. A plain /Projects (menu, breadcrumb, bookmark) opens the tab last
        // chosen, or Active on a first visit. Any link that carries a parameter keeps its meaning
        // (All unless it names a tab), so Command desk and Analytics drill-downs are unchanged.
        public const string TabCookieName = "prism_projects_tab";

        private void ApplyRememberedTab()
        {
            var request = HttpContext?.Request;
            if (request is null || request.QueryString.HasValue)
            {
                return;
            }

            Lifecycle = Enum.TryParse<ProjectLifecycleFilter>(request.Cookies[TabCookieName], ignoreCase: false, out var remembered)
                && remembered is ProjectLifecycleFilter.Active or ProjectLifecycleFilter.Completed or ProjectLifecycleFilter.Cancelled or ProjectLifecycleFilter.All
                    ? remembered
                    : ProjectLifecycleFilter.Active;
        }

        private void RememberChosenTab()
        {
            if (HttpContext?.Request.Query.ContainsKey("Lifecycle") != true)
            {
                return;
            }

            Response.Cookies.Append(TabCookieName, Lifecycle.ToString(), new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = Request.PathBase.Add("/Projects"),
                MaxAge = TimeSpan.FromDays(365),
                HttpOnly = true,
                IsEssential = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
        }

        public bool ShowTableView =>
            string.Equals(HttpContext?.Request.Cookies[ViewCookieName], "table", StringComparison.Ordinal);

        // Section: Labels for filters that arrive only by URL (Command desk and Analytics links),
        // so a narrowed list always shows why. A month without a stage does not filter.
        public string? StageFilterLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(StageCode))
                {
                    return null;
                }

                var name = _workflowStageMetadataProvider.GetDisplayName(null, StageCode.Trim());
                return ParseStageMonth(StageCompletedMonth) is { } month
                    ? $"{name} completed in {month.ToString("MMM yyyy", CultureInfo.InvariantCulture)}"
                    : $"Reached {name}";
            }
        }

        public string? SlipBucketFilterLabel =>
            string.IsNullOrWhiteSpace(SlipBucket) ? null : $"Slip: {SlipBucket.Trim()} days";

        public bool AppliesCategoryDescendants => IncludeCategoryDescendants && CategoryId.HasValue;

        public string OrderDescription => BuildOrderDescription();

        public string ProjectCountLabel => FilteredTotal == 1 ? "project" : "projects";

        public string ActiveLifecycleLabel
        {
            get
            {
                if (IsCompletedRegister && Record is { } record)
                {
                    return record == ProjectRegisterRecord.Legacy ? "Completed · Legacy entries" : "Completed · Recorded in PRISM";
                }

                var activeLifecycle = LifecycleTabs.FirstOrDefault(tab => tab.IsActive);
                return string.IsNullOrWhiteSpace(activeLifecycle?.Label)
                    ? "All"
                    : activeLifecycle!.Label;
            }
        }

        public async Task OnGetAsync()
        {
            await LoadRepositoryAsync(loadFilterOptions: true);
        }

        public async Task<PartialViewResult> OnGetLiveAsync()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache";
            await LoadRepositoryAsync(loadFilterOptions: false);
            return Partial("_ProjectRepositoryLive", this);
        }

        // Section: Excel export of the current view (every page, in screen order), built from the
        // same register facts and row text as the tables.
        public async Task<IActionResult> OnGetExportAsync()
        {
            PageSize = AllPageSizeValue;
            CurrentPage = 1;
            await LoadRepositoryAsync(loadFilterOptions: false);

            var rows = Projects
                .Select(project => ProjectRegisterExportRow.From(
                    project,
                    ClosedRegisterRows.TryGetValue(project.Id, out var closed) ? closed : ProjectRegisterClosedRowVm.Create(project),
                    StagePositions.TryGetValue(project.Id, out var position) ? position : ProjectRepositoryStagePositionVm.Missing,
                    ActiveRegisterFacts.TryGetValue(project.Id, out var facts) ? facts : null))
                .ToList();

            var exportedAt = _clock.UtcNow;
            var file = _excelBuilder.Build(new ProjectRegisterExportContext(
                Lifecycle,
                ActiveLifecycleLabel,
                rows,
                FilterChips.Select(chip => chip.Label).ToList(),
                OrderDescription,
                exportedAt));

            var stamp = ProjectManagement.Infrastructure.IstClock.ToIst(exportedAt).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            return File(
                file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"prism-projects-{Lifecycle.ToString().ToLowerInvariant()}-{stamp}.xlsx");
        }

        // Route values for the export link: the current view's filters and order, not its page.
        public IDictionary<string, string?> BuildExportRoute()
        {
            var values = BuildBaseRouteValues();
            values.Remove("PageSize");
            values.Remove("p");
            return values;
        }

        private async Task LoadRepositoryAsync(bool loadFilterOptions)
        {
            var cancellationToken = HttpContext.RequestAborted;

            // Section: Normalize query parameters
            ApplyRememberedTab();
            NormalizeProjectTypeFilters();
            NormalizeOrdering();
            NormalizeRegisterFilters();
            RememberChosenTab();
            var buildFilter = NormalizeBuildFilter();

            // Full page loads need the selectable option lists. Live result
            // refreshes retain the existing form and therefore skip these queries.
            if (loadFilterOptions)
            {
                await LoadFilterOptionsAsync(cancellationToken);
            }

            var stageMonth = ParseStageMonth(StageCompletedMonth);

            IReadOnlyCollection<int>? resolvedCategoryIds = null;
            if (IncludeCategoryDescendants && CategoryId.HasValue)
            {
                resolvedCategoryIds = await _categoryHierarchy
                    .GetCategoryAndDescendantIdsAsync(CategoryId.Value, cancellationToken);
            }

            var baseFilters = new ProjectSearchFilters(
                Query,
                CategoryId,
                TechnicalCategoryId,
                LeadPoUserId,
                HodUserId,
                ProjectLifecycleFilter.All,
                CompletedYear,
                TotStatus,
                IncludeArchived,
                StageCode,
                stageMonth,
                SlipBucket,
                IncludeCategoryDescendants,
                resolvedCategoryIds,
                CompletedFrom,
                CompletedTo,
                StageCompletedFrom,
                StageCompletedTo);

            var lifecycleCounts = await CountProjectsByLifecycleAsync(
                baseFilters,
                buildFilter,
                cancellationToken);
            LifecycleTabs = BuildLifecycleTabs(lifecycleCounts);

            var filters = baseFilters with { Lifecycle = Lifecycle };

            // Section: Filter counters and filtered dataset
            var baseQuery = await BuildFilteredQueryAsync(
                filters,
                buildFilter,
                applyBuildFilter: true,
                applyProjectTypeFilter: false,
                cancellationToken: cancellationToken);
            await LoadFilterCountsAsync(
                baseQuery,
                loadProjectTypeDefinitions: loadFilterOptions,
                cancellationToken: cancellationToken);

            var filteredQuery = ApplyProjectTypeFilter(baseQuery);

            // Section: Completed tab record counts (they follow every other filter)
            if (IsCompletedRegister)
            {
                RecordLegacyCount = await filteredQuery.CountAsync(p => p.IsLegacy, cancellationToken);
                RecordPrismCount = await filteredQuery.CountAsync(p => !p.IsLegacy, cancellationToken);
            }

            // Section: Active register facts and pipeline (resolved in memory; the Active set is
            // small). The pipeline counts the Active set under every other filter.
            var registerFacts = IsActiveRegister
                ? await _registerFacts.GetActiveFactsAsync(cancellationToken)
                : new Dictionary<int, ProjectRegisterActiveFacts>();

            if (IsActiveRegister)
            {
                var inScope = await filteredQuery.Select(p => p.Id).ToListAsync(cancellationToken);
                Pipeline = ProjectRegisterPipeline.Build(
                    inScope.Where(registerFacts.ContainsKey).Select(id => registerFacts[id]).ToList(),
                    _workflowStageMetadataProvider.GetStages(null),
                    CurrentStage,
                    Attention);
            }

            // The pipeline filters as a set of matching ids over every active project, so the same
            // set narrows the list and each quick-filter count.
            List<int>? registerMatchIds = null;
            if (IsActiveRegister && (!string.IsNullOrWhiteSpace(CurrentStage) || Attention.HasValue))
            {
                var stage = CurrentStage;
                var attention = Attention;
                registerMatchIds = registerFacts.Values
                    .Where(f => stage is null || f.IsCurrentlyIn(stage))
                    .Where(f => attention is null || f.Matches(attention.Value))
                    .Select(f => f.ProjectId)
                    .ToList();
            }

            IQueryable<Project> ApplyTabFilters(IQueryable<Project> source)
            {
                source = IsCompletedRegister
                    ? Record switch
                    {
                        ProjectRegisterRecord.Legacy => source.Where(p => p.IsLegacy),
                        ProjectRegisterRecord.Prism => source.Where(p => !p.IsLegacy),
                        _ => source
                    }
                    : source;

                if (registerMatchIds is not null)
                {
                    var ids = registerMatchIds;
                    source = source.Where(p => ids.Contains(p.Id));
                }

                return source;
            }

            filteredQuery = ApplyTabFilters(filteredQuery);

            // Section: Quick-filter menus. Each counts the list under every other filter, so a
            // chosen officer still shows every officer's count.
            async Task<IQueryable<Project>> FacetScopeAsync(ProjectSearchFilters facetFilters, bool applyBuild)
                => ApplyTabFilters(await BuildFilteredQueryAsync(
                    facetFilters,
                    buildFilter,
                    applyBuildFilter: applyBuild,
                    applyProjectTypeFilter: true,
                    cancellationToken: cancellationToken));

            var officerRows = await (await FacetScopeAsync(filters with { LeadPoUserId = null }, applyBuild: true))
                .Where(p => p.LeadPoUserId != null)
                .GroupBy(p => new { Id = p.LeadPoUserId!, Name = p.LeadPoUser != null ? (p.LeadPoUser.FullName ?? p.LeadPoUser.UserName) : null })
                .Select(g => new FacetRow(g.Key.Id, g.Key.Name, g.Count()))
                .ToListAsync(cancellationToken);

            // The technical category filter matches the category itself, so each option counts its
            // own projects; a sub-category is labelled with its parent ("Parent › Child").
            var technicalRows = await (await FacetScopeAsync(filters with { TechnicalCategoryId = null }, applyBuild: true))
                .Where(p => p.TechnicalCategoryId != null)
                .GroupBy(p => p.TechnicalCategoryId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var technicalPaths = await LoadTechnicalCategoryPathsAsync(cancellationToken);

            var categoryRows = await (await FacetScopeAsync(
                    filters with { CategoryId = null, IncludeCategoryDescendants = false, CategoryIds = null },
                    applyBuild: true))
                .Where(p => p.CategoryId != null)
                .GroupBy(p => new { Id = p.CategoryId!.Value, Name = p.Category != null ? p.Category.Name : null })
                .Select(g => new { g.Key.Id, g.Key.Name, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var buildRows = await (await FacetScopeAsync(filters, applyBuild: false))
                .GroupBy(p => p.IsBuild)
                .Select(g => new { IsBuild = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            NewBuildCount = buildRows.Where(r => !r.IsBuild).Sum(r => r.Count);
            RepeatBuildCount = buildRows.Where(r => r.IsBuild).Sum(r => r.Count);

            var selectedNames = await ResolveSelectedNamesAsync(cancellationToken);

            QuickFacets = new[]
            {
                BuildFacet("LeadPoUserId", "Project Officer", "Any project officer", officerRows, LeadPoUserId, selectedNames.Officer),
                BuildFacet(
                    "CategoryId",
                    "Category",
                    "Any category",
                    categoryRows.Select(r => new FacetRow(r.Id.ToString(CultureInfo.InvariantCulture), r.Name, r.Count)).ToList(),
                    CategoryId?.ToString(CultureInfo.InvariantCulture),
                    selectedNames.Category),
                BuildFacet(
                    "TechnicalCategoryId",
                    "Technical category",
                    "Any technical category",
                    technicalRows.Select(r => new FacetRow(
                        r.Id.ToString(CultureInfo.InvariantCulture),
                        technicalPaths.TryGetValue(r.Id, out var path) ? path : null,
                        r.Count)).ToList(),
                    TechnicalCategoryId?.ToString(CultureInfo.InvariantCulture),
                    TechnicalCategoryId is int technicalId && technicalPaths.TryGetValue(technicalId, out var selectedPath)
                        ? selectedPath
                        : selectedNames.TechnicalCategory),
                new ProjectRegisterFacet("Build", "Build", new[]
                {
                    new ProjectRegisterFacetOption(null, "Any build", NewBuildCount + RepeatBuildCount, buildFilter is null),
                    new ProjectRegisterFacetOption("New", "New development", NewBuildCount, buildFilter == BuildFilter.New),
                    new ProjectRegisterFacetOption("Repeat", "Repeat build", RepeatBuildCount, buildFilter == BuildFilter.Repeat)
                })
            };

            FilterChips = BuildFilterChips(selectedNames);

            FilteredTotal = await filteredQuery.CountAsync(cancellationToken);
            TotalCount = FilteredTotal;

            // Section: Results query setup
            var query = filteredQuery
                .Include(p => p.Category)
                .Include(p => p.TechnicalCategory)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .Include(p => p.Tot)
                .Include(p => p.ProjectStages)
                .Include(p => p.ProjectType)
                .AsQueryable();

            query = query.ApplyProjectOrdering(filters, Sort, Dir, _db.Database.IsNpgsql());

            // Section: Normalize paging values
            var isAll = PageSize == AllPageSizeValue;
            if (!isAll)
            {
                PageSize = PageSize switch
                {
                    <= 0 => DefaultPageSize,
                    > MaxPageSize => MaxPageSize,
                    _ => PageSize
                };
            }

            TotalPages = TotalCount == 0
                ? 0
                : isAll
                    ? 1
                    : (int)Math.Ceiling(TotalCount / (double)PageSize);

            if (isAll)
            {
                CurrentPage = 1;
            }
            else
            {
                if (CurrentPage < 1)
                {
                    CurrentPage = 1;
                }

                if (TotalPages > 0 && CurrentPage > TotalPages)
                {
                    CurrentPage = TotalPages;
                }
                else if (TotalPages == 0)
                {
                    CurrentPage = 1;
                }
            }

            var skip = isAll ? 0 : (CurrentPage - 1) * PageSize;
            if (!isAll && TotalCount > 0 && skip >= TotalCount)
            {
                CurrentPage = TotalPages;
                skip = Math.Max(0, (CurrentPage - 1) * PageSize);
            }

            if (UsesAttentionOrder)
            {
                // The attention order comes from resolved facts, so it is applied to the whole
                // filtered set in memory before paging (the Active set is small).
                var candidates = await filteredQuery
                    .Select(p => new { p.Id, p.Name })
                    .ToListAsync(cancellationToken);

                var orderedIds = candidates
                    .OrderBy(c => registerFacts.GetValueOrDefault(c.Id), ProjectRegisterActiveFacts.AttentionOrder)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Id)
                    .Select(c => c.Id)
                    .ToList();

                var pageIds = isAll ? orderedIds : orderedIds.Skip(skip).Take(PageSize).ToList();
                var position = pageIds
                    .Select((id, index) => (id, index))
                    .ToDictionary(item => item.id, item => item.index);
                var pageProjects = await query
                    .Where(p => pageIds.Contains(p.Id))
                    .ToListAsync(cancellationToken);

                Projects = pageProjects.OrderBy(p => position[p.Id]).ToList();
            }
            else
            {
                Projects = isAll
                    ? await query.ToListAsync(cancellationToken)
                    : await query.Skip(skip).Take(PageSize).ToListAsync(cancellationToken);
            }

            ActiveRegisterFacts = Projects
                .Where(project => registerFacts.ContainsKey(project.Id))
                .ToDictionary(project => project.Id, project => registerFacts[project.Id]);

            ActiveRegisterRows = Projects
                .Where(project => registerFacts.ContainsKey(project.Id))
                .ToDictionary(
                    project => project.Id,
                    project => ProjectRegisterActiveRowVm.Create(registerFacts[project.Id]));

            ClosedRegisterRows = IsActiveRegister
                ? new Dictionary<int, ProjectRegisterClosedRowVm>()
                : Projects.ToDictionary(project => project.Id, ProjectRegisterClosedRowVm.Create);

            StagePositions = Projects.ToDictionary(
                project => project.Id,
                project => ProjectRepositoryStagePositionVm.Create(
                    project,
                    _workflowStageMetadataProvider));

            ResultsStart = TotalCount == 0 ? 0 : isAll ? 1 : skip + 1;
            ResultsEnd = TotalCount == 0 ? 0 : isAll ? TotalCount : Math.Min(skip + Projects.Count, TotalCount);
        }

        // Section: Repository ordering helpers
        public bool IsSortActive(ProjectRepositorySort sort) => Sort == sort;

        public ProjectSortDirection NextSortDirection(ProjectRepositorySort sort)
        {
            if (Sort != sort)
            {
                return ProjectSortDirection.Asc;
            }

            return Dir == ProjectSortDirection.Asc
                ? ProjectSortDirection.Desc
                : ProjectSortDirection.Asc;
        }

        public string GetSortAria(ProjectRepositorySort sort)
        {
            if (Sort != sort)
            {
                return "none";
            }

            return Dir == ProjectSortDirection.Asc ? "ascending" : "descending";
        }

        public string GetSortIconClass(ProjectRepositorySort sort)
        {
            if (Sort != sort)
            {
                return "bi bi-arrow-down-up";
            }

            return Dir == ProjectSortDirection.Asc
                ? "bi bi-sort-alpha-down"
                : "bi bi-sort-alpha-up";
        }

        public IDictionary<string, string?> BuildBaseRouteValues()
        {
            var values = new Dictionary<string, string?>();

            static void AddValue(IDictionary<string, string?> target, string key, object? value)
            {
                if (value is null)
                {
                    return;
                }

                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    target[key] = text;
                }
            }

            AddValue(values, "Query", Query);
            AddValue(values, "CategoryId", CategoryId);
            AddValue(values, "TechnicalCategoryId", TechnicalCategoryId);
            AddValue(values, "LeadPoUserId", LeadPoUserId);
            AddValue(values, "HodUserId", HodUserId);
            AddValue(values, "Lifecycle", Lifecycle.ToString());
            AddValue(values, "CompletedYear", CompletedYear);
            AddValue(values, "TotStatus", TotStatus?.ToString());
            AddValue(values, "IncludeArchived", IncludeArchived ? "true" : null);
            AddValue(values, "StageCode", StageCode);
            AddValue(values, "StageCompletedMonth", StageCompletedMonth);
            AddValue(values, "CompletedFrom", DayValue(CompletedFrom));
            AddValue(values, "CompletedTo", DayValue(CompletedTo));
            AddValue(values, "StageCompletedFrom", DayValue(StageCompletedFrom));
            AddValue(values, "StageCompletedTo", DayValue(StageCompletedTo));
            AddValue(values, "SlipBucket", SlipBucket);
            AddValue(values, "IncludeCategoryDescendants", IncludeCategoryDescendants ? "true" : null);
            AddValue(values, "Build", Build);
            AddValue(values, "ProjectTypeId", ProjectTypeId);
            AddValue(values, "ProjectTypeUnclassified", ProjectTypeUnclassified ? "true" : null);
            AddValue(values, "CurrentStage", CurrentStage);
            AddValue(values, "Attention", Attention?.ToString());
            AddValue(values, "Record", Record?.ToString());
            AddValue(values, "PageSize", PageSize);

            if (Sort != ProjectRepositorySort.Operational)
            {
                AddValue(values, "Sort", Sort);
                AddValue(values, "Dir", Dir);
            }

            return values;
        }

        public IDictionary<string, string?> BuildSortRoute(ProjectRepositorySort sort)
        {
            var values = BuildBaseRouteValues();
            values["Sort"] = sort.ToString();
            values["Dir"] = NextSortDirection(sort).ToString();
            values["p"] = "1";
            return values;
        }

        public IDictionary<string, string?> BuildOperationalOrderRoute()
        {
            var values = BuildBaseRouteValues();
            values.Remove("Sort");
            values.Remove("Dir");
            values["p"] = "1";
            return values;
        }

        // The pipeline filters belong to the Active tab; elsewhere (and when unreadable) they are dropped.
        private void NormalizeRegisterFilters()
        {
            if (ModelState.TryGetValue(nameof(Attention), out var attentionEntry) && attentionEntry.Errors.Count > 0)
            {
                Attention = null;
                ModelState.Remove(nameof(Attention));
            }

            if (ModelState.TryGetValue(nameof(Record), out var recordEntry) && recordEntry.Errors.Count > 0)
            {
                Record = null;
                ModelState.Remove(nameof(Record));
            }

            // The Legacy archive is now part of Completed.
            if (Lifecycle == ProjectLifecycleFilter.Legacy)
            {
                Lifecycle = ProjectLifecycleFilter.Completed;
                Record ??= ProjectRegisterRecord.Legacy;
            }

            if (!IsCompletedRegister)
            {
                Record = null;
            }

            CurrentStage = string.IsNullOrWhiteSpace(CurrentStage) ? null : CurrentStage.Trim().ToUpperInvariant();

            if (!IsActiveRegister)
            {
                CurrentStage = null;
                Attention = null;
            }
        }

        // Route values for a pipeline control: toggles that one filter, keeps the rest, back to page 1.
        public IDictionary<string, string?> BuildPipelineRoute(string? currentStage, ProjectRegisterAttention? attention)
        {
            var values = BuildBaseRouteValues();
            values["CurrentStage"] = currentStage;
            values["Attention"] = attention?.ToString();
            values["p"] = "1";
            return values;
        }

        private void NormalizeOrdering()
        {
            if (!Enum.IsDefined(Sort))
            {
                Sort = ProjectRepositorySort.Operational;
                ModelState.Remove(nameof(Sort));
            }

            if (!Enum.IsDefined(Dir))
            {
                Dir = ProjectSortDirection.Asc;
                ModelState.Remove(nameof(Dir));
            }

            if (Sort == ProjectRepositorySort.Operational)
            {
                Dir = ProjectSortDirection.Asc;
            }
        }

        private string BuildOrderDescription()
        {
            if (Sort != ProjectRepositorySort.Operational)
            {
                var direction = Dir == ProjectSortDirection.Asc ? "ascending" : "descending";
                return Sort switch
                {
                    ProjectRepositorySort.Project => $"Project name, {direction}",
                    ProjectRepositorySort.Status => $"Lifecycle status, {direction}",
                    ProjectRepositorySort.Officer => $"Project officer, {direction}",
                    ProjectRepositorySort.Category => $"Category, {direction}",
                    ProjectRepositorySort.CaseFile => $"Case file, {direction}",
                    _ => "Operational order"
                };
            }

            if (!string.IsNullOrWhiteSpace(Query))
            {
                return "Search relevance, then operational order";
            }

            return Lifecycle switch
            {
                ProjectLifecycleFilter.Active => "Blocked first, then most overdue",
                ProjectLifecycleFilter.Completed => "Latest completion first",
                ProjectLifecycleFilter.Legacy => "Latest completion first",
                ProjectLifecycleFilter.Cancelled => "Latest cancellation first",
                _ => "Active first; completed by latest completion"
            };
        }

        // Section: KPI helpers
        private async Task LoadFilterCountsAsync(
            IQueryable<Project> baseQuery,
            bool loadProjectTypeDefinitions,
            CancellationToken cancellationToken)
        {
            var typeCounts = await baseQuery
                .GroupBy(project => project.ProjectTypeId)
                .Select(group => new
                {
                    ProjectTypeId = group.Key,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);

            ProjectTypeUnclassifiedCount = typeCounts
                .Where(item => !item.ProjectTypeId.HasValue)
                .Select(item => item.Count)
                .FirstOrDefault();

            ProjectTypeCounts = typeCounts
                .Where(item => item.ProjectTypeId.HasValue)
                .ToDictionary(item => item.ProjectTypeId!.Value, item => item.Count);

            if (loadProjectTypeDefinitions)
            {
                var types = await _db.ProjectTypes
                    .AsNoTracking()
                    .OrderBy(type => type.SortOrder)
                    .ThenBy(type => type.Name)
                    .ToListAsync(cancellationToken);

                ProjectTypeChips = types
                    .Select(type => new ProjectTypeChipViewModel(
                        type.Id,
                        type.Name,
                        ProjectTypeCounts.TryGetValue(type.Id, out var count) ? count : 0))
                    .ToList();
            }
        }

        private async Task<IQueryable<Project>> BuildFilteredQueryAsync(
            ProjectSearchFilters filters,
            BuildFilter? buildFilter,
            bool applyBuildFilter,
            bool applyProjectTypeFilter,
            CancellationToken cancellationToken)
        {
            var query = _db.Projects
                .AsNoTracking()
                .ApplyProjectSearch(filters, _db.Database.IsNpgsql());

            if (!string.IsNullOrWhiteSpace(filters.SlipBucket))
            {
                var slipIds = await _analytics
                    .GetProjectIdsForSlipBucketAsync(
                        filters.Lifecycle,
                        filters.CategoryId,
                        filters.TechnicalCategoryId,
                        filters.SlipBucket!,
                        cancellationToken: cancellationToken,
                        expandedCategoryIds: filters.CategoryIds);

                if (slipIds.Count == 0)
                {
                    query = query.Where(_ => false);
                }
                else
                {
                    var idArray = slipIds.ToArray();
                    query = query.Where(p => idArray.Contains(p.Id));
                }
            }

            if (applyBuildFilter)
            {
                query = ApplyBuildFilter(query, buildFilter);
            }

            if (applyProjectTypeFilter)
            {
                query = ApplyProjectTypeFilter(query);
            }

            return query;
        }

        private IQueryable<Project> ApplyProjectTypeFilter(IQueryable<Project> query)
        {
            if (ProjectTypeUnclassified)
            {
                return query.Where(p => p.ProjectTypeId == null);
            }

            if (ProjectTypeId.HasValue)
            {
                return query.Where(p => p.ProjectTypeId == ProjectTypeId.Value);
            }

            return query;
        }

        private static IQueryable<Project> ApplyBuildFilter(IQueryable<Project> query, BuildFilter? buildFilter)
        {
            return buildFilter switch
            {
                BuildFilter.Repeat => query.Where(p => p.IsBuild),
                BuildFilter.New => query.Where(p => !p.IsBuild),
                _ => query
            };
        }

        private void NormalizeProjectTypeFilters()
        {
            if (ModelState.TryGetValue(nameof(ProjectTypeId), out var entry) && entry.Errors.Count > 0)
            {
                ProjectTypeId = null;
                ModelState.Remove(nameof(ProjectTypeId));
            }

            if (ProjectTypeUnclassified)
            {
                ProjectTypeId = null;
            }
        }

        private BuildFilter? NormalizeBuildFilter()
        {
            var buildFilter = ParseBuildFilter(Build);
            Build = buildFilter?.ToString();
            return buildFilter;
        }

        private static BuildFilter? ParseBuildFilter(string? buildValue)
        {
            if (string.IsNullOrWhiteSpace(buildValue))
            {
                return null;
            }

            if (string.Equals(buildValue, "Repeat", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFilter.Repeat;
            }

            if (string.Equals(buildValue, "New", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFilter.New;
            }

            return null;
        }

        public static string? DayValue(DateOnly? date)
            => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // "26 Sep – 25 Oct 2026" for an active-filter label; one side alone reads "from"/"to".
        public static string? DescribeWindow(DateOnly? from, DateOnly? to)
        {
            static string Day(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
            return (from, to) switch
            {
                ({ } f, { } t) when f.Year == t.Year => $"{f.ToString("dd MMM", CultureInfo.InvariantCulture)} – {Day(t)}",
                ({ } f, { } t) => $"{Day(f)} – {Day(t)}",
                ({ } f, null) => $"from {Day(f)}",
                (null, { } t) => $"to {Day(t)}",
                _ => null
            };
        }

        private static DateOnly? ParseStageMonth(string? month)
        {
            if (string.IsNullOrWhiteSpace(month))
            {
                return null;
            }

            if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private async Task LoadFilterOptionsAsync(CancellationToken cancellationToken)
        {
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryOption(c.Id, c.Name))
                .ToListAsync(cancellationToken);

            CategoryOptions = BuildCategoryOptions(categories, CategoryId);

            // One row per person, not per project.
            var hodUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.HodUserId != null)
                .Select(p => new
                {
                    Id = p.HodUserId!,
                    FullName = p.HodUser != null ? p.HodUser.FullName : null,
                    UserName = p.HodUser != null ? p.HodUser.UserName : null
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            HodOptions = BuildUserOptions(
                hodUsers.Select(u => new UserOption(u.Id, u.FullName, u.UserName)),
                HodUserId,
                "Any HoD");

            var leadPoUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.LeadPoUserId != null)
                .Select(p => new
                {
                    Id = p.LeadPoUserId!,
                    FullName = p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                    UserName = p.LeadPoUser != null ? p.LeadPoUser.UserName : null
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            LeadPoOptions = BuildUserOptions(
                leadPoUsers.Select(u => new UserOption(u.Id, u.FullName, u.UserName)),
                LeadPoUserId,
                "Any Project Officer");

            var completionYears = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.CompletedYear.HasValue)
                .Select(p => p.CompletedYear!.Value)
                .Distinct()
                .OrderByDescending(year => year)
                .ToListAsync(cancellationToken);

            CompletionYearOptions = BuildCompletionYearOptions(completionYears, CompletedYear);

            TotStatusOptions = BuildTotStatusOptions(TotStatus);
        }

        private static IEnumerable<SelectListItem> BuildCategoryOptions(IEnumerable<CategoryOption> categories, int? selectedId)
        {
            var options = new List<SelectListItem>
            {
                new("All categories", string.Empty, !selectedId.HasValue)
            };

            var selectedValue = selectedId?.ToString();
            options.AddRange(categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())
            {
                Selected = selectedValue is not null && string.Equals(selectedValue, c.Id.ToString(), StringComparison.Ordinal)
            }));

            return options;
        }

        private static IEnumerable<SelectListItem> BuildUserOptions(IEnumerable<UserOption> users, string? selectedId, string emptyLabel)
        {
            var options = new List<SelectListItem>
            {
                new(emptyLabel, string.Empty, string.IsNullOrWhiteSpace(selectedId))
            };

            var uniqueUsers = users
                .Where(u => !string.IsNullOrWhiteSpace(u.Id))
                .GroupBy(u => u.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(u => DisplayName(u), StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => DisplayName(u));

            foreach (var user in uniqueUsers)
            {
                var selected = selectedId is not null && string.Equals(user.Id, selectedId, StringComparison.Ordinal);
                options.Add(new SelectListItem(DisplayName(user), user.Id, selected));
            }

            return options;
        }

        private static string DisplayName(UserOption option)
        {
            if (!string.IsNullOrWhiteSpace(option.FullName))
            {
                return option.FullName!;
            }

            if (!string.IsNullOrWhiteSpace(option.UserName))
            {
                return option.UserName!;
            }

            return option.Id;
        }

        private async Task<IReadOnlyDictionary<ProjectLifecycleFilter, int>> CountProjectsByLifecycleAsync(
            ProjectSearchFilters baseFilters,
            BuildFilter? buildFilter,
            CancellationToken cancellationToken)
        {
            var counts = new Dictionary<ProjectLifecycleFilter, int>();

            foreach (var filter in new[]
                     {
                         ProjectLifecycleFilter.All,
                         ProjectLifecycleFilter.Active,
                         ProjectLifecycleFilter.Completed,
                         ProjectLifecycleFilter.Cancelled
                     })
            {
                var countFilters = baseFilters with { Lifecycle = filter };
                var query = await BuildFilteredQueryAsync(
                    countFilters,
                    buildFilter,
                    applyBuildFilter: true,
                    applyProjectTypeFilter: true,
                    cancellationToken: cancellationToken);

                counts[filter] = await query.CountAsync(cancellationToken);
            }

            return counts;
        }

        private IReadOnlyList<LifecycleFilterTab> BuildLifecycleTabs(IReadOnlyDictionary<ProjectLifecycleFilter, int> counts)
        {
            return new[]
            {
                CreateLifecycleTab(ProjectLifecycleFilter.Active, "Active", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.Completed, "Completed", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.Cancelled, "Cancelled", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.All, "All", counts)
            };
        }

        private LifecycleFilterTab CreateLifecycleTab(ProjectLifecycleFilter filter, string label, IReadOnlyDictionary<ProjectLifecycleFilter, int> counts)
        {
            counts.TryGetValue(filter, out var count);
            return new LifecycleFilterTab(filter, label, filter.ToString(), Lifecycle == filter, count);
        }

        private static IEnumerable<SelectListItem> BuildCompletionYearOptions(IEnumerable<int> years, int? selectedYear)
        {
            var options = new List<SelectListItem>
            {
                new("Any completion year", string.Empty, !selectedYear.HasValue)
            };

            foreach (var year in years)
            {
                var isSelected = selectedYear.HasValue && selectedYear.Value == year;
                options.Add(new SelectListItem(year.ToString(), year.ToString(), isSelected));
            }

            return options;
        }

        private static IEnumerable<SelectListItem> BuildTotStatusOptions(ProjectTotStatus? selectedStatus)
        {
            var options = new List<SelectListItem>
            {
                new("All ToT statuses", string.Empty, !selectedStatus.HasValue)
            };

            foreach (var status in Enum.GetValues<ProjectTotStatus>())
            {
                var isSelected = selectedStatus.HasValue && selectedStatus.Value == status;
                options.Add(new SelectListItem(GetTotStatusLabel(status), status.ToString(), isSelected));
            }

            return options;
        }

        private static string GetTotStatusLabel(ProjectTotStatus status)
        {
            return status switch
            {
                ProjectTotStatus.NotRequired => "Not required",
                ProjectTotStatus.NotStarted => "Not started",
                ProjectTotStatus.InProgress => "In progress",
                ProjectTotStatus.Completed => "Completed",
                _ => status.ToString()
            };
        }

        public string? FormatTotStatusShort(Project project)
        {
            if (project.IsBuild)
            {
                return "ToT not applicable";
            }

            if (project.Tot is not { } tot)
            {
                return null;
            }

            return tot.Status switch
            {
                ProjectTotStatus.NotRequired => "ToT not required",
                ProjectTotStatus.NotStarted => "ToT not started",
                ProjectTotStatus.InProgress => "ToT in progress",
                ProjectTotStatus.Completed => "ToT completed",
                _ => null
            };
        }

        public string FormatLifecycleStatus(ProjectLifecycleStatus status)
        {
            return status switch
            {
                ProjectLifecycleStatus.Active => "Active",
                ProjectLifecycleStatus.Completed => "Completed",
                ProjectLifecycleStatus.Cancelled => "Cancelled",
                _ => status.ToString()
            };
        }

        public string GetLifecycleBadgeClass(ProjectLifecycleStatus status)
        {
            return status switch
            {
                ProjectLifecycleStatus.Active => "text-bg-primary",
                ProjectLifecycleStatus.Completed => "text-bg-success",
                ProjectLifecycleStatus.Cancelled => "project-lifecycle-badge project-lifecycle-badge--cancelled",
                _ => "project-lifecycle-badge project-lifecycle-badge--unknown"
            };
        }

        // Section: Quick-filter menus and chips
        private sealed record FacetRow(string Id, string? Name, int Count);

        private sealed record SelectedNames(string? Officer, string? Hod, string? Category, string? TechnicalCategory, string? ProjectType);

        private static ProjectRegisterFacet BuildFacet(
            string key,
            string name,
            string anyLabel,
            IReadOnlyList<FacetRow> rows,
            string? selected,
            string? selectedName)
        {
            var options = rows
                .Select(row => new ProjectRegisterFacetOption(
                    row.Id,
                    string.IsNullOrWhiteSpace(row.Name) ? row.Id : row.Name!,
                    row.Count,
                    string.Equals(row.Id, selected, StringComparison.Ordinal)))
                .ToList();

            // A chosen value that no project in scope has still shows, so it can be seen and changed.
            if (selected is not null && options.All(option => !option.IsSelected))
            {
                options.Add(new ProjectRegisterFacetOption(selected, selectedName ?? selected, 0, true));
            }

            options = options
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            options.Insert(0, new ProjectRegisterFacetOption(null, anyLabel, null, selected is null));
            return new ProjectRegisterFacet(key, name, options);
        }

        // Each technical category's name with its parents' names in front ("Parent › Child").
        private async Task<IReadOnlyDictionary<int, string>> LoadTechnicalCategoryPathsAsync(CancellationToken cancellationToken)
        {
            var categories = await _db.TechnicalCategories
                .AsNoTracking()
                .Select(c => new { c.Id, c.Name, c.ParentId })
                .ToDictionaryAsync(c => c.Id, cancellationToken);

            string PathOf(int id)
            {
                var names = new List<string>();
                var seen = new HashSet<int>();
                for (int? current = id; current is int key && seen.Add(key) && categories.TryGetValue(key, out var category); current = category.ParentId)
                {
                    names.Insert(0, category.Name);
                }

                return string.Join(" › ", names);
            }

            return categories.Keys.ToDictionary(id => id, PathOf);
        }

        // Names for the chosen filters, for the chips and for a chosen menu value with no projects.
        private async Task<SelectedNames> ResolveSelectedNamesAsync(CancellationToken cancellationToken)
        {
            var userIds = new[] { LeadPoUserId, HodUserId }.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            var users = userIds.Length == 0
                ? new Dictionary<string, string>()
                : await _db.Users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? u.Id, cancellationToken);

            string? category = CategoryId is int categoryId
                ? await _db.ProjectCategories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
                : null;
            string? technical = TechnicalCategoryId is int technicalId
                ? await _db.TechnicalCategories.AsNoTracking().Where(c => c.Id == technicalId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
                : null;
            string? projectType = ProjectTypeId is int typeId
                ? await _db.ProjectTypes.AsNoTracking().Where(t => t.Id == typeId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken)
                : null;

            return new SelectedNames(
                LeadPoUserId is { } po && users.TryGetValue(po, out var poName) ? poName : LeadPoUserId,
                HodUserId is { } hod && users.TryGetValue(hod, out var hodName) ? hodName : HodUserId,
                category,
                technical,
                projectType);
        }

        // One chip per applied filter, each with the route that removes only that filter. The
        // Completed tab's record switch shows its own choice, so it has no chip.
        private IReadOnlyList<ProjectRegisterFilterChip> BuildFilterChips(SelectedNames names)
        {
            var chips = new List<ProjectRegisterFilterChip>();

            void Add(bool applies, string label, params string[] keys)
            {
                if (!applies)
                {
                    return;
                }

                var route = BuildBaseRouteValues();
                foreach (var key in keys)
                {
                    route.Remove(key);
                }

                route["p"] = "1";
                chips.Add(new ProjectRegisterFilterChip(label, route));
            }

            Add(!string.IsNullOrWhiteSpace(Query), $"Search: {Query?.Trim()}", "Query");
            Add(!string.IsNullOrWhiteSpace(LeadPoUserId), $"Project Officer: {names.Officer}", "LeadPoUserId");
            Add(!string.IsNullOrWhiteSpace(HodUserId), $"HoD: {names.Hod}", "HodUserId");
            Add(CategoryId.HasValue, $"Category: {names.Category ?? CategoryId?.ToString(CultureInfo.InvariantCulture)}", "CategoryId", "IncludeCategoryDescendants");
            Add(AppliesCategoryDescendants, "Including sub-categories", "IncludeCategoryDescendants");
            Add(!string.IsNullOrWhiteSpace(Build), Build == "Repeat" ? "Repeat build" : "New development", "Build");
            Add(TechnicalCategoryId.HasValue, $"Technical category: {names.TechnicalCategory ?? TechnicalCategoryId?.ToString(CultureInfo.InvariantCulture)}", "TechnicalCategoryId");
            Add(ProjectTypeUnclassified, "Project type: Unclassified", "ProjectTypeUnclassified", "ProjectTypeId");
            Add(!ProjectTypeUnclassified && ProjectTypeId.HasValue, $"Project type: {names.ProjectType ?? ProjectTypeId?.ToString(CultureInfo.InvariantCulture)}", "ProjectTypeId");
            Add(CompletedYear.HasValue, $"Completion year: {CompletedYear}", "CompletedYear");
            Add(TotStatus.HasValue, $"ToT: {(TotStatus is { } tot ? GetTotStatusLabel(tot) : null)}", "TotStatus");
            Add(IncludeArchived, "Archived included", "IncludeArchived");
            Add(DescribeWindow(CompletedFrom, CompletedTo) is not null, $"Completed {DescribeWindow(CompletedFrom, CompletedTo)}", "CompletedFrom", "CompletedTo");
            Add(DescribeWindow(StageCompletedFrom, StageCompletedTo) is not null, $"Stage completed {DescribeWindow(StageCompletedFrom, StageCompletedTo)}", "StageCompletedFrom", "StageCompletedTo");
            Add(StageFilterLabel is not null, StageFilterLabel ?? string.Empty, "StageCode", "StageCompletedMonth");
            Add(SlipBucketFilterLabel is not null, SlipBucketFilterLabel ?? string.Empty, "SlipBucket");
            Add(CurrentStageFilterLabel is not null, CurrentStageFilterLabel ?? string.Empty, "CurrentStage");
            Add(AttentionFilterLabel is not null, AttentionFilterLabel ?? string.Empty, "Attention");

            return chips;
        }

        public sealed record LifecycleFilterTab(ProjectLifecycleFilter Filter, string Label, string? RouteValue, bool IsActive, int Count);

        private sealed record UserOption(string Id, string? FullName, string? UserName);

        private sealed record CategoryOption(int Id, string Name);

        public sealed record ProjectTypeChipViewModel(int Id, string Name, int Count);

        private enum BuildFilter
        {
            Repeat,
            New
        }
    }
}
