using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Ongoing
{
    [Authorize]
    public sealed class IndexModel : PageModel
    {
        private readonly OngoingProjectsReadService _ongoingService;
        private readonly IOngoingProjectsExcelBuilder _excelBuilder;
        private readonly IClock _clock;
        private readonly ApplicationDbContext _db;

        // SECTION: Category cache for header counts
        private IReadOnlyList<ProjectCategory> _categories = Array.Empty<ProjectCategory>();

        public IndexModel(
            OngoingProjectsReadService ongoingService,
            IOngoingProjectsExcelBuilder excelBuilder,
            IClock clock,
            ApplicationDbContext db)
        {
            _ongoingService = ongoingService ?? throw new ArgumentNullException(nameof(ongoingService));
            _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [BindProperty(SupportsGet = true)]
        public int? ProjectCategoryId { get; set; }

        // LeadPoUserId
        [BindProperty(SupportsGet = true)]
        public string? ProjectOfficerId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        // SECTION: View selector (timeline/table)
        [BindProperty(SupportsGet = true)]
        public string? View { get; set; }

        public IReadOnlyList<SelectListItem> ProjectCategoryOptions { get; private set; }
            = Array.Empty<SelectListItem>();

        public IReadOnlyList<SelectListItem> ProjectOfficerOptions { get; private set; }
            = Array.Empty<SelectListItem>();

        // SECTION: Header counts summary
        public int FilteredTotal { get; private set; }

        // SECTION: KPI chip counts
        public int ChipTotalCount { get; private set; }
        public int ChipCoECount { get; private set; }
        public int ChipDcdCount { get; private set; }
        public int ChipOtherRdCount { get; private set; }

        public IReadOnlyList<CategoryCountDto> FilteredCategoryCounts { get; private set; }
            = Array.Empty<CategoryCountDto>();

        public string HeaderCountsText { get; private set; } = string.Empty;

        public IReadOnlyList<OngoingProjectRowDto> Items { get; private set; }
            = Array.Empty<OngoingProjectRowDto>();

        // SECTION: Inline external remark editing metadata
        public bool CanInlineEditExternalRemarks { get; private set; }
        public string TodayIstIso { get; private set; } = string.Empty;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            View = NormalizeView(View);
            await LoadCategoriesAsync(cancellationToken);

            var officerId = Normalize(ProjectOfficerId);
            var search = Normalize(Search);

            ProjectOfficerOptions = await _ongoingService.GetProjectOfficerOptionsAsync(
                officerId,
                cancellationToken);

            Items = await _ongoingService.GetAsync(
                ProjectCategoryId,
                officerId,
                search,
                cancellationToken);

            // SECTION: Inline external remark editing access + IST date
            CanInlineEditExternalRemarks = User.IsInRole("HoD");
            TodayIstIso = ResolveTodayIstIso(_clock.UtcNow);

            BuildHeaderCounts();
        }

        public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
        {
            var officerId = Normalize(ProjectOfficerId);
            var search = Normalize(Search);

            var items = await _ongoingService.GetAsync(
                ProjectCategoryId,
                officerId,
                search,
                cancellationToken);

            var now = _clock.UtcNow;

            var file = _excelBuilder.Build(
                new OngoingProjectsExportContext(
                    items,
                    now,
                    ProjectCategoryId,
                    search));

            var fileName = $"ongoing-projects-{now:yyyyMMddHHmmss}.xlsx";

            return File(
                file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private async Task LoadCategoriesAsync(CancellationToken ct)
        {
            var cats = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync(ct);

            _categories = cats;

            var list = new List<SelectListItem>
            {
                new("All categories", string.Empty)
            };

            foreach (var c in cats)
            {
                list.Add(new SelectListItem(c.Name, c.Id.ToString(), c.Id == ProjectCategoryId));
            }

            ProjectCategoryOptions = list;
        }

        private void BuildHeaderCounts()
        {
            // SECTION: Build filtered totals and category breakdown
            FilteredTotal = Items.Count;
            ChipTotalCount = Items.Count;

            var categoryLookup = _categories.ToDictionary(c => c.Id);

            var orderedCategories = _categories
                .Where(category => category.ParentId is null)
                .OrderBy(category => category.Name)
                .ToList();

            // SECTION: Resolve top-level IDs for KPI chips (prefix match)
            static bool StartsWithIgnoreCase(string value, string prefix)
                => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            var coeCategory = orderedCategories.FirstOrDefault(category => StartsWithIgnoreCase(category.Name, "CoE"));
            var dcdCategory = orderedCategories.FirstOrDefault(category => StartsWithIgnoreCase(category.Name, "DCD"));
            var otherCategory = orderedCategories.FirstOrDefault(category => StartsWithIgnoreCase(category.Name, "Other"));

            var countsByCategory = Items
                .Where(item => item.ProjectCategoryId.HasValue)
                .Select(item =>
                {
                    if (!item.ProjectCategoryId.HasValue)
                    {
                        return (int?)null;
                    }

                    if (!categoryLookup.TryGetValue(item.ProjectCategoryId.Value, out var category))
                    {
                        return (int?)null;
                    }

                    var topLevel = ResolveTopLevelCategory(category, categoryLookup);
                    return topLevel?.Id;
                })
                .Where(categoryId => categoryId.HasValue)
                .GroupBy(categoryId => categoryId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());

            ChipCoECount = (coeCategory != null && countsByCategory.TryGetValue(coeCategory.Id, out var coeCount))
                ? coeCount
                : 0;
            ChipDcdCount = (dcdCategory != null && countsByCategory.TryGetValue(dcdCategory.Id, out var dcdCount))
                ? dcdCount
                : 0;
            ChipOtherRdCount = (otherCategory != null && countsByCategory.TryGetValue(otherCategory.Id, out var otherCount))
                ? otherCount
                : 0;

            var orderedCounts = new List<CategoryCountDto>();

            foreach (var category in orderedCategories)
            {
                if (!countsByCategory.TryGetValue(category.Id, out var count))
                {
                    continue;
                }

                if (count <= 0)
                {
                    continue;
                }

                orderedCounts.Add(new CategoryCountDto
                {
                    ProjectCategoryId = category.Id,
                    CategoryName = category.Name,
                    Count = count
                });
            }

            FilteredCategoryCounts = orderedCounts;

            var parts = new List<string>
            {
                $"Total - {FilteredTotal}"
            };

            foreach (var category in FilteredCategoryCounts)
            {
                parts.Add($"{category.CategoryName} - {category.Count}");
            }

            HeaderCountsText = $"({string.Join(", ", parts)})";
        }

        private static ProjectCategory? ResolveTopLevelCategory(
            ProjectCategory category,
            IReadOnlyDictionary<int, ProjectCategory> lookup)
        {
            var current = category;
            var visited = new HashSet<int>();

            while (current.ParentId.HasValue
                   && lookup.TryGetValue(current.ParentId.Value, out var parent)
                   && visited.Add(current.Id))
            {
                current = parent;
            }

            return current;
        }

        private static string? Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // SECTION: View normalization
        private static string NormalizeView(string? value)
        {
            if (string.Equals(value, "table", StringComparison.OrdinalIgnoreCase))
            {
                return "table";
            }

            return "timeline";
        }

        // SECTION: IST helper (yyyy-MM-dd)
        private static string ResolveTodayIstIso(DateTimeOffset utcNow)
        {
            var indiaTimeZone = GetIndiaTimeZone();
            var local = TimeZoneInfo.ConvertTime(utcNow, indiaTimeZone);
            var today = DateOnly.FromDateTime(local.DateTime);
            return today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static TimeZoneInfo GetIndiaTimeZone()
        {
            string[] timeZoneIds = { "India Standard Time", "Asia/Kolkata" };

            foreach (var timeZoneId in timeZoneIds)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            var offset = TimeSpan.FromHours(5.5);
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Kolkata", offset, "India Standard Time", "India Standard Time");
        }
    }

    // SECTION: DTOs
    public sealed class CategoryCountDto
    {
        public int ProjectCategoryId { get; init; }
        public string CategoryName { get; init; } = "";
        public int Count { get; init; }
    }
}
