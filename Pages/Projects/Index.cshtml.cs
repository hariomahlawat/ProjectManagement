using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class IndexModel : PageModel
    {
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

        public IList<Project> Projects { get; private set; } = new List<Project>();

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

        [BindProperty(SupportsGet = true, Name = "page")]
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

        public int TotalCount { get; private set; }

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
            !string.IsNullOrWhiteSpace(SlipBucket);

        public IEnumerable<SelectListItem> CategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> TechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> LeadPoOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> HodOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> CompletionYearOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> TotStatusOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IReadOnlyList<LifecycleFilterTab> LifecycleTabs { get; private set; } = Array.Empty<LifecycleFilterTab>();

        public async Task OnGetAsync()
        {
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

            var lifecycleCounts = await CountProjectsByLifecycleAsync(baseFilters);
            LifecycleTabs = BuildLifecycleTabs(lifecycleCounts);

            var query = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Include(p => p.Category)
                .Include(p => p.TechnicalCategory)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .Include(p => p.Tot)
                .Include(p => p.ProjectStages)
                .AsQueryable();

            var filters = baseFilters with { Lifecycle = Lifecycle };
            query = query.ApplyProjectSearch(filters);

            if (!string.IsNullOrWhiteSpace(SlipBucket))
            {
                var slipIds = await _analytics
                    .GetProjectIdsForSlipBucketAsync(
                        filters.Lifecycle,
                        filters.CategoryId,
                        filters.TechnicalCategoryId,
                        SlipBucket!,
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

            query = query.ApplyProjectOrdering(filters);

            PageSize = PageSize switch
            {
                <= 0 => 25,
                > 200 => 200,
                _ => PageSize
            };

            TotalCount = await query.CountAsync();
            TotalPages = TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

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

            var skip = (CurrentPage - 1) * PageSize;
            if (TotalCount > 0 && skip >= TotalCount)
            {
                CurrentPage = TotalPages;
                skip = Math.Max(0, (CurrentPage - 1) * PageSize);
            }

            Projects = await query.Skip(skip).Take(PageSize).ToListAsync();

            ResultsStart = TotalCount == 0 ? 0 : skip + 1;
            ResultsEnd = TotalCount == 0 ? 0 : Math.Min(skip + Projects.Count, TotalCount);
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

        private async Task<IReadOnlyDictionary<ProjectLifecycleFilter, int>> CountProjectsByLifecycleAsync(ProjectSearchFilters baseFilters)
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
                var query = _db.Projects
                    .AsNoTracking()
                    .ApplyProjectSearch(countFilters);

                if (!string.IsNullOrWhiteSpace(baseFilters.SlipBucket))
                {
                    var slipIds = await _analytics.GetProjectIdsForSlipBucketAsync(
                        countFilters.Lifecycle,
                        countFilters.CategoryId,
                        countFilters.TechnicalCategoryId,
                        baseFilters.SlipBucket!,
                        cancellationToken: HttpContext.RequestAborted,
                        expandedCategoryIds: countFilters.CategoryIds);

                    if (slipIds.Count == 0)
                    {
                        counts[filter] = 0;
                        continue;
                    }

                    var idArray = slipIds.ToArray();
                    query = query.Where(p => idArray.Contains(p.Id));
                }

                var count = await query.CountAsync();

                counts[filter] = count;
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
                CreateLifecycleTab(ProjectLifecycleFilter.Legacy, "Legacy", counts),
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
    }
}
