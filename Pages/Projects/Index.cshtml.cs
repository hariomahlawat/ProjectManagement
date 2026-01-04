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
using ProjectManagement.Models.Remarks;
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

        public IndexModel(
            ApplicationDbContext db,
            IProjectAnalyticsService analytics,
            ProjectCategoryHierarchyService categoryHierarchy)
        {
            _db = db;
            _analytics = analytics;
            _categoryHierarchy = categoryHierarchy;
        }

        public IReadOnlyList<Project> Projects { get; private set; } = new List<Project>();

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

        public int TotalCount { get; private set; }

        // Section: KPI counters (filtered dataset)
        public int FilteredTotal { get; private set; }

        public int OldCount { get; private set; }

        public int RepeatBuildCount { get; private set; }

        public int NewBuildCount { get; private set; }

        public IReadOnlyList<ProjectTypeChipViewModel> ProjectTypeChips { get; private set; }
            = Array.Empty<ProjectTypeChipViewModel>();

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

        public IReadOnlyDictionary<int, ProjectRemarkSummaryViewModel> RemarkSummaries { get; private set; }
            = new Dictionary<int, ProjectRemarkSummaryViewModel>();

        public async Task OnGetAsync()
        {
            // Section: Normalize query parameters
            NormalizeProjectTypeFilters();
            var buildFilter = NormalizeBuildFilter();

            // Section: Filter option loading
            await LoadFilterOptionsAsync();

            var stageMonth = ParseStageMonth(StageCompletedMonth);

            IReadOnlyCollection<int>? resolvedCategoryIds = null;
            if (IncludeCategoryDescendants && CategoryId.HasValue)
            {
                resolvedCategoryIds = await _categoryHierarchy
                    .GetCategoryAndDescendantIdsAsync(CategoryId.Value, HttpContext.RequestAborted);
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

            var lifecycleCounts = await CountProjectsByLifecycleAsync(baseFilters, buildFilter);
            LifecycleTabs = BuildLifecycleTabs(lifecycleCounts);

            var filters = baseFilters with { Lifecycle = Lifecycle };

            // Section: KPI counts for filtered dataset
            var baseQuery = await BuildFilteredQueryAsync(filters, buildFilter, applyBuildFilter: true, applyProjectTypeFilter: false);
            var baseQueryNoBuild = await BuildFilteredQueryAsync(filters, buildFilter, applyBuildFilter: false, applyProjectTypeFilter: false);

            ProjectTypeChips = await BuildProjectTypeChipsAsync(baseQuery);
            ProjectTypeUnclassifiedCount = await baseQuery.Where(p => p.ProjectTypeId == null).CountAsync();

            RepeatBuildCount = await baseQueryNoBuild.Where(p => p.IsBuild).CountAsync();
            NewBuildCount = await baseQueryNoBuild.Where(p => !p.IsBuild).CountAsync();

            var filteredQuery = ApplyProjectTypeFilter(baseQuery);
            FilteredTotal = await filteredQuery.CountAsync();
            OldCount = await filteredQuery.Where(p => p.IsLegacy).CountAsync();
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

            query = query.ApplyProjectOrdering(filters);

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
                ? await query.ToListAsync()
                : await query.Skip(skip).Take(PageSize).ToListAsync();
            RemarkSummaries = await LoadRemarkSummariesAsync(Projects, HttpContext.RequestAborted);

            ResultsStart = TotalCount == 0 ? 0 : isAll ? 1 : skip + 1;
            ResultsEnd = TotalCount == 0 ? 0 : isAll ? TotalCount : Math.Min(skip + Projects.Count, TotalCount);
        }

        // Section: KPI helpers
        private async Task<IReadOnlyList<ProjectTypeChipViewModel>> BuildProjectTypeChipsAsync(IQueryable<Project> baseQuery)
        {
            var counts = await baseQuery
                .Where(p => p.ProjectTypeId.HasValue)
                .GroupBy(p => p.ProjectTypeId)
                .Select(g => new
                {
                    Id = g.Key!.Value,
                    Count = g.Count()
                })
                .ToListAsync();

            var countLookup = counts.ToDictionary(item => item.Id, item => item.Count);

            var types = await _db.ProjectTypes
                .AsNoTracking()
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Name)
                .ToListAsync();

            return types
                .Select(t => new ProjectTypeChipViewModel(
                    t.Id,
                    t.Name,
                    countLookup.TryGetValue(t.Id, out var count) ? count : 0))
                .ToList();
        }

        private async Task<IQueryable<Project>> BuildFilteredQueryAsync(
            ProjectSearchFilters filters,
            BuildFilter? buildFilter,
            bool applyBuildFilter,
            bool applyProjectTypeFilter)
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
                        cancellationToken: HttpContext.RequestAborted,
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

        private async Task LoadFilterOptionsAsync()
        {
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryOption(c.Id, c.Name))
                .ToListAsync();

            CategoryOptions = BuildCategoryOptions(categories, CategoryId);

            var technicalCategories = await _db.TechnicalCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new TechnicalCategoryOption(c.Id, c.Name, c.ParentId, c.IsActive))
                .ToListAsync();

            TechnicalCategoryOptions = BuildTechnicalCategoryOptions(technicalCategories, TechnicalCategoryId);

            var hodUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.HodUserId != null)
                .Select(p => new UserOption(
                    p.HodUserId!,
                    p.HodUser != null ? p.HodUser.FullName : null,
                    p.HodUser != null ? p.HodUser.UserName : null))
                .ToListAsync();

            HodOptions = BuildUserOptions(hodUsers, HodUserId, "Any HoD");

            var leadPoUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.LeadPoUserId != null)
                .Select(p => new UserOption(
                    p.LeadPoUserId!,
                    p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                    p.LeadPoUser != null ? p.LeadPoUser.UserName : null))
                .ToListAsync();

            LeadPoOptions = BuildUserOptions(leadPoUsers, LeadPoUserId, "Any Project Officer");

            var completionYears = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.CompletedYear.HasValue)
                .Select(p => p.CompletedYear!.Value)
                .Distinct()
                .OrderByDescending(year => year)
                .ToListAsync();

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

        private async Task<IReadOnlyDictionary<int, ProjectRemarkSummaryViewModel>> LoadRemarkSummariesAsync(
            IReadOnlyCollection<Project> projects,
            CancellationToken cancellationToken)
        {
            if (projects.Count == 0)
            {
                return new Dictionary<int, ProjectRemarkSummaryViewModel>();
            }

            var projectIds = projects.Select(p => p.Id).ToArray();

            var remarks = await _db.Remarks
                .AsNoTracking()
                .Where(r => projectIds.Contains(r.ProjectId) && !r.IsDeleted)
                .Select(r => new RemarkProjection(
                    r.ProjectId,
                    r.Id,
                    r.Type,
                    r.AuthorRole,
                    r.AuthorUserId,
                    r.Body,
                    r.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            var authorLookup = await BuildAuthorLookupAsync(remarks, cancellationToken);

            var summaries = remarks
                .GroupBy(r => r.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var ordered = g
                            .OrderByDescending(r => r.CreatedAtUtc)
                            .ThenByDescending(r => r.Id)
                            .ToList();

                        var last = ordered.FirstOrDefault();

                        return new ProjectRemarkSummaryViewModel
                        {
                            InternalCount = g.Count(r => r.Type == RemarkType.Internal),
                            ExternalCount = g.Count(r => r.Type == RemarkType.External),
                            LastRemarkId = last?.Id,
                            LastRemarkType = last?.Type,
                            LastRemarkActorRole = last?.AuthorRole,
                            LastActivityUtc = last?.CreatedAtUtc,
                            LastRemarkPreview = BuildRemarkPreview(last?.Body),
                            LastRemarkAuthorDisplayName = ResolveAuthorDisplayName(last?.AuthorUserId, authorLookup)
                        };
                    });

            foreach (var project in projects)
            {
                if (!summaries.ContainsKey(project.Id))
                {
                    summaries[project.Id] = ProjectRemarkSummaryViewModel.Empty;
                }
            }

            return summaries;
        }

        private async Task<IReadOnlyDictionary<string, string>> BuildAuthorLookupAsync(
            IEnumerable<RemarkProjection> remarks,
            CancellationToken cancellationToken)
        {
            var authorIds = remarks
                .Select(r => r.AuthorUserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (authorIds.Length == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var authors = await _db.Users
                .AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.UserName,
                    u.Email
                })
                .ToListAsync(cancellationToken);

            return authors.ToDictionary(
                a => a.Id,
                a => !string.IsNullOrWhiteSpace(a.FullName)
                    ? a.FullName!
                    : !string.IsNullOrWhiteSpace(a.UserName)
                        ? a.UserName!
                        : !string.IsNullOrWhiteSpace(a.Email)
                            ? a.Email!
                            : a.Id,
                StringComparer.Ordinal);
        }

        private static string? ResolveAuthorDisplayName(
            string? authorUserId,
            IReadOnlyDictionary<string, string> authorLookup)
        {
            if (string.IsNullOrWhiteSpace(authorUserId))
            {
                return null;
            }

            return authorLookup.TryGetValue(authorUserId, out var display)
                ? display
                : authorUserId;
        }

        private static string? BuildRemarkPreview(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var trimmed = body.Trim();
            trimmed = trimmed.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

            const int limit = 120;
            if (trimmed.Length <= limit)
            {
                return trimmed;
            }

            return string.Concat(trimmed.AsSpan(0, limit), "…");
        }

        private sealed record RemarkProjection(
            int ProjectId,
            int Id,
            RemarkType Type,
            RemarkActorRole AuthorRole,
            string? AuthorUserId,
            string? Body,
            DateTime CreatedAtUtc);

        private async Task<IReadOnlyDictionary<ProjectLifecycleFilter, int>> CountProjectsByLifecycleAsync(
            ProjectSearchFilters baseFilters,
            BuildFilter? buildFilter)
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
                    applyProjectTypeFilter: true);

                counts[filter] = await query.CountAsync();
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
                CreateLifecycleTab(ProjectLifecycleFilter.Cancelled, "Cancelled", counts),
                CreateLifecycleTab(ProjectLifecycleFilter.Legacy, "Old projects", counts),
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
