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

        public IndexModel(
            ApplicationDbContext db,
            IProjectAnalyticsService analytics,
            ProjectCategoryHierarchyService categoryHierarchy,
            IWorkflowStageMetadataProvider workflowStageMetadataProvider)
        {
            _db = db;
            _analytics = analytics;
            _categoryHierarchy = categoryHierarchy;
            _workflowStageMetadataProvider = workflowStageMetadataProvider;
        }

        public IReadOnlyList<Project> Projects { get; private set; } = new List<Project>();

        public IReadOnlyDictionary<int, ProjectRepositoryStagePositionVm> StagePositions { get; private set; }
            = new Dictionary<int, ProjectRepositoryStagePositionVm>();

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
            !string.IsNullOrWhiteSpace(SlipBucket) ||
            ProjectTypeId.HasValue ||
            ProjectTypeUnclassified ||
            !string.IsNullOrWhiteSpace(Build);

        public IEnumerable<SelectListItem> CategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> TechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> LeadPoOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> HodOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> CompletionYearOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> TotStatusOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IReadOnlyList<LifecycleFilterTab> LifecycleTabs { get; private set; } = Array.Empty<LifecycleFilterTab>();

        public LifecycleFilterTab LegacyArchive { get; private set; } =
            new(ProjectLifecycleFilter.Legacy, "Legacy archive", ProjectLifecycleFilter.Legacy.ToString(), false, 0);

        public string OrderDescription => BuildOrderDescription();

        public string ProjectCountLabel => FilteredTotal == 1 ? "project" : "projects";

        public string ActiveLifecycleLabel
        {
            get
            {
                if (LegacyArchive.IsActive)
                {
                    return LegacyArchive.Label;
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

        private async Task LoadRepositoryAsync(bool loadFilterOptions)
        {
            var cancellationToken = HttpContext.RequestAborted;

            // Section: Normalize query parameters
            NormalizeProjectTypeFilters();
            NormalizeOrdering();
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
                resolvedCategoryIds);

            var lifecycleCounts = await CountProjectsByLifecycleAsync(
                baseFilters,
                buildFilter,
                cancellationToken);
            LifecycleTabs = BuildLifecycleTabs(lifecycleCounts);
            LegacyArchive = CreateLifecycleTab(ProjectLifecycleFilter.Legacy, "Legacy archive", lifecycleCounts);

            var filters = baseFilters with { Lifecycle = Lifecycle };

            // Section: Filter counters and filtered dataset
            var baseQuery = await BuildFilteredQueryAsync(
                filters,
                buildFilter,
                applyBuildFilter: true,
                applyProjectTypeFilter: false,
                cancellationToken: cancellationToken);
            var baseQueryNoBuild = await BuildFilteredQueryAsync(
                filters,
                buildFilter,
                applyBuildFilter: false,
                applyProjectTypeFilter: false,
                cancellationToken: cancellationToken);

            await LoadFilterCountsAsync(
                baseQuery,
                baseQueryNoBuild,
                loadProjectTypeDefinitions: loadFilterOptions,
                cancellationToken: cancellationToken);

            var filteredQuery = ApplyProjectTypeFilter(baseQuery);
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

            query = query.ApplyProjectOrdering(filters, Sort, Dir);

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

            Projects = isAll
                ? await query.ToListAsync(cancellationToken)
                : await query.Skip(skip).Take(PageSize).ToListAsync(cancellationToken);

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
            AddValue(values, "Lifecycle", Lifecycle == ProjectLifecycleFilter.All ? null : Lifecycle.ToString());
            AddValue(values, "CompletedYear", CompletedYear);
            AddValue(values, "TotStatus", TotStatus?.ToString());
            AddValue(values, "IncludeArchived", IncludeArchived ? "true" : null);
            AddValue(values, "StageCode", StageCode);
            AddValue(values, "StageCompletedMonth", StageCompletedMonth);
            AddValue(values, "SlipBucket", SlipBucket);
            AddValue(values, "IncludeCategoryDescendants", IncludeCategoryDescendants ? "true" : null);
            AddValue(values, "Build", Build);
            AddValue(values, "ProjectTypeId", ProjectTypeId);
            AddValue(values, "ProjectTypeUnclassified", ProjectTypeUnclassified ? "true" : null);
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
                ProjectLifecycleFilter.Active => "Recent recorded update first",
                ProjectLifecycleFilter.Completed => "Latest completion first",
                ProjectLifecycleFilter.Legacy => "Latest completion first",
                ProjectLifecycleFilter.Cancelled => "Latest cancellation first",
                _ => "Active first; completed by latest completion"
            };
        }

        // Section: KPI helpers
        private async Task LoadFilterCountsAsync(
            IQueryable<Project> baseQuery,
            IQueryable<Project> baseQueryNoBuild,
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

            RepeatBuildCount = await baseQueryNoBuild
                .Where(project => project.IsBuild)
                .CountAsync(cancellationToken);

            NewBuildCount = await baseQueryNoBuild
                .Where(project => !project.IsBuild)
                .CountAsync(cancellationToken);
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
                .ApplyProjectSearch(filters);

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

            var technicalCategories = await _db.TechnicalCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new TechnicalCategoryOption(c.Id, c.Name, c.ParentId, c.IsActive))
                .ToListAsync(cancellationToken);

            TechnicalCategoryOptions = BuildTechnicalCategoryOptions(technicalCategories, TechnicalCategoryId);

            var hodUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.HodUserId != null)
                .Select(p => new UserOption(
                    p.HodUserId!,
                    p.HodUser != null ? p.HodUser.FullName : null,
                    p.HodUser != null ? p.HodUser.UserName : null))
                .ToListAsync(cancellationToken);

            HodOptions = BuildUserOptions(hodUsers, HodUserId, "Any HoD");

            var leadPoUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.LeadPoUserId != null)
                .Select(p => new UserOption(
                    p.LeadPoUserId!,
                    p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                    p.LeadPoUser != null ? p.LeadPoUser.UserName : null))
                .ToListAsync(cancellationToken);

            LeadPoOptions = BuildUserOptions(leadPoUsers, LeadPoUserId, "Any Project Officer");

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

        private static IEnumerable<SelectListItem> BuildTechnicalCategoryOptions(
            IEnumerable<TechnicalCategoryOption> categories,
            int? selectedId)
        {
            var lookup = categories
                .Where(c => c.IsActive)
                .ToLookup(c => c.ParentId);

            var options = new List<SelectListItem>
            {
                new("All technical categories", string.Empty, !selectedId.HasValue)
            };

            void AddOptions(int? parentId, string prefix)
            {
                foreach (var category in lookup[parentId])
                {
                    var text = string.IsNullOrEmpty(prefix) ? category.Name : $"{prefix}{category.Name}";
                    var isSelected = selectedId.HasValue && selectedId.Value == category.Id;
                    options.Add(new SelectListItem(text, category.Id.ToString(), isSelected));
                    AddOptions(category.Id, string.Concat(prefix, "— "));
                }
            }

            AddOptions(null, string.Empty);

            if (selectedId.HasValue)
            {
                var selectedValue = selectedId.Value.ToString();
                if (options.All(option => !string.Equals(option.Value, selectedValue, StringComparison.Ordinal)))
                {
                    var selected = categories.FirstOrDefault(c => c.Id == selectedId.Value);
                    if (selected is not null)
                    {
                        options.Add(new SelectListItem($"{selected.Name} (inactive)", selected.Id.ToString(), true));
                    }
                }
            }

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
                         ProjectLifecycleFilter.Cancelled,
                         ProjectLifecycleFilter.Legacy
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
                CreateLifecycleTab(ProjectLifecycleFilter.All, "All", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.Active, "Active", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.Completed, "Completed", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.Cancelled, "Cancelled", counts)
            };
        }

        private LifecycleFilterTab CreateLifecycleTab(ProjectLifecycleFilter filter, string label, IReadOnlyDictionary<ProjectLifecycleFilter, int> counts)
        {
            counts.TryGetValue(filter, out var count);
            return new LifecycleFilterTab(filter, label, filter == ProjectLifecycleFilter.All ? null : filter.ToString(), Lifecycle == filter, count);
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

        public sealed record LifecycleFilterTab(ProjectLifecycleFilter Filter, string Label, string? RouteValue, bool IsActive, int Count);

        private sealed record UserOption(string Id, string? FullName, string? UserName);

        private sealed record CategoryOption(int Id, string Name);

        private sealed record TechnicalCategoryOption(int Id, string Name, int? ParentId, bool IsActive);

        public sealed record ProjectTypeChipViewModel(int Id, string Name, int Count);

        private enum BuildFilter
        {
            Repeat,
            New
        }
    }
}
