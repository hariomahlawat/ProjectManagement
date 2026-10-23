using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
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
        private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;

        // SECTION: Category cache for header counts
        private IReadOnlyList<ProjectCategory> _categories = Array.Empty<ProjectCategory>();

        public IndexModel(
            OngoingProjectsReadService ongoingService,
            IOngoingProjectsExcelBuilder excelBuilder,
            IClock clock,
            ApplicationDbContext db,
            IWorkflowStageMetadataProvider workflowStageMetadataProvider)
        {
            _ongoingService = ongoingService ?? throw new ArgumentNullException(nameof(ongoingService));
            _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _workflowStageMetadataProvider = workflowStageMetadataProvider ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
        }

        [BindProperty(SupportsGet = true)]
        public int? ProjectCategoryId { get; set; }

        // LeadPoUserId
        [BindProperty(SupportsGet = true)]
        public string? ProjectOfficerId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        // SECTION: Present stage selector
        [BindProperty(SupportsGet = true)]
        public string? PresentStageCode { get; set; }

        // SECTION: Broad stage bucket selector (single value; kept so existing links still work)
        [BindProperty(SupportsGet = true)]
        public string? StageBucket { get; set; }

        // SECTION: Multi-select chips. Repeated query values: ?Stage=procurement&Stage=development&Category=3
        [BindProperty(SupportsGet = true)]
        public List<string> Stage { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public List<int> Category { get; set; } = new();

        // SECTION: Sub-category, narrowing within the selected category chips
        [BindProperty(SupportsGet = true)]
        public int? SubCategory { get; set; }

        /// <summary>Stage buckets in effect: the chips plus any legacy single value, in pipeline order.</summary>
        public IReadOnlyList<string> SelectedStageBuckets { get; private set; } = Array.Empty<string>();

        /// <summary>Top-level category chips selected (a legacy top-level value included).</summary>
        public IReadOnlyList<int> SelectedCategoryIds { get; private set; } = Array.Empty<int>();

        /// <summary>The category ids the query uses: the sub-category when one is chosen, else the chips.</summary>
        public IReadOnlyList<int> EffectiveCategoryIds { get; private set; } = Array.Empty<int>();

        public IReadOnlyList<CategoryChipVm> CategoryChips { get; private set; } = Array.Empty<CategoryChipVm>();
        public IReadOnlyList<SelectListItem> SubCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
        public bool HasSubCategories { get; private set; }

        public IReadOnlyDictionary<int, int> CategoryCounts { get; private set; } = new Dictionary<int, int>();
        public int AllProjectsCount { get; private set; }

        public bool HasFilters => SelectedStageBuckets.Count > 0
            || SelectedCategoryIds.Count > 0
            || SubCategory.HasValue
            || !string.IsNullOrWhiteSpace(PresentStageCode)
            || !string.IsNullOrWhiteSpace(ProjectOfficerId)
            || !string.IsNullOrWhiteSpace(Search);

        public static readonly IReadOnlyList<string> StageBucketOrder = new[] { "approval", "aon", "procurement", "development" };

        // SECTION: View selector (timeline/table)
        [BindProperty(SupportsGet = true)]
        public string? View { get; set; }

        // SECTION: Timeline stage flow selector
        [BindProperty(SupportsGet = true)]
        public string StageFlow { get; set; } = "reverse";

        // SECTION: Table column sort (OngoingProjectSort); none keeps the operational order
        [BindProperty(SupportsGet = true)]
        public string? Sort { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortDir { get; set; }

        public IReadOnlyList<SelectListItem> ProjectCategoryOptions { get; private set; }
            = Array.Empty<SelectListItem>();

        public IReadOnlyList<SelectListItem> ProjectOfficerOptions { get; private set; }
            = Array.Empty<SelectListItem>();

        public IReadOnlyList<SelectListItem> PresentStageOptions { get; private set; }
            = Array.Empty<SelectListItem>();

        // SECTION: Header counts summary
        public int FilteredTotal { get; private set; }

        // SECTION: KPI chip counts
        public int ChipTotalCount { get; private set; }
        public int ChipCoECount { get; private set; }
        public int ChipDcdCount { get; private set; }
        public int ChipOtherRdCount { get; private set; }
        public int? ChipCoECategoryId { get; private set; }
        public int? ChipDcdCategoryId { get; private set; }
        public int? ChipOtherRdCategoryId { get; private set; }

        // SECTION: Stage bucket counts
        public int BucketApvlCount { get; private set; }
        public int BucketAonCount { get; private set; }
        public int BucketProcCount { get; private set; }
        public int BucketDevpCount { get; private set; }
        public int BucketUnknownCount { get; private set; }

        public IReadOnlyList<CategoryCountDto> FilteredCategoryCounts { get; private set; }
            = Array.Empty<CategoryCountDto>();

        public string HeaderCountsText { get; private set; } = string.Empty;

        public IReadOnlyList<OngoingProjectRowDto> Items { get; private set; }
            = Array.Empty<OngoingProjectRowDto>();

        // SECTION: Stage bucket counts source
        public OngoingStageBucketCountsDto BucketCounts { get; private set; } = OngoingStageBucketCountsDto.Empty;

        // SECTION: Inline external remark editing metadata
        public bool CanInlineEditExternalRemarks { get; private set; }
        public string TodayIstIso { get; private set; } = string.Empty;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            View = NormalizeView(View);
            StageFlow = NormalizeStageFlow(StageFlow);
            NormalizeSort();
            await LoadCategoriesAsync(cancellationToken);
            ResolveFilters();

            var officerId = Normalize(ProjectOfficerId);
            var search = Normalize(Search);

            ProjectOfficerOptions = await _ongoingService.GetProjectOfficerOptionsAsync(
                officerId,
                cancellationToken);

            Items = await _ongoingService.GetAsync(
                EffectiveCategoryIds,
                officerId,
                search,
                PresentStageCode,
                SelectedStageBuckets,
                StageFlow,
                cancellationToken);
            Items = OngoingProjectSort.Apply(Items, Sort, SortDir);

            // Each chip group counts with every filter except its own, so a count says
            // what selecting that chip would show.
            BucketCounts = await _ongoingService.GetStageBucketCountsAsync(
                EffectiveCategoryIds,
                officerId,
                search,
                cancellationToken);
            CategoryCounts = await _ongoingService.GetCategoryCountsAsync(
                officerId,
                search,
                PresentStageCode,
                SelectedStageBuckets,
                cancellationToken);
            AllProjectsCount = await _ongoingService.CountAllAsync(cancellationToken);

            // SECTION: Inline external remark editing access + IST date
            CanInlineEditExternalRemarks = User.IsInRole("HoD");
            TodayIstIso = ResolveTodayIstIso(_clock.UtcNow);

            BuildHeaderCounts();
        }

        public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
        {
            View = NormalizeView(View);
            StageFlow = NormalizeStageFlow(StageFlow);
            NormalizeSort();
            await LoadCategoriesAsync(cancellationToken);
            ResolveFilters();
            var officerId = Normalize(ProjectOfficerId);
            var search = Normalize(Search);

            var items = await _ongoingService.GetAsync(
                EffectiveCategoryIds,
                officerId,
                search,
                PresentStageCode,
                SelectedStageBuckets,
                StageFlow,
                cancellationToken);
            // The export follows the order on screen, including a table column sort.
            items = OngoingProjectSort.Apply(items, Sort, SortDir);

            var now = _clock.UtcNow;

            var file = _excelBuilder.Build(
                new OngoingProjectsExportContext(
                    items,
                    now,
                    EffectiveCategoryIds.Count == 1 ? EffectiveCategoryIds[0] : null,
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

            // SECTION: Stage bucket breakdown
            BucketApvlCount = BucketCounts.Approval;
            BucketAonCount = BucketCounts.Aon;
            BucketProcCount = BucketCounts.Procurement;
            BucketDevpCount = BucketCounts.Development;
            BucketUnknownCount = BucketCounts.Unknown;

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

            ChipCoECategoryId = coeCategory?.Id;
            ChipDcdCategoryId = dcdCategory?.Id;
            ChipOtherRdCategoryId = otherCategory?.Id;

            var countsByCategory = CategoryCounts
                .Select(entry => (
                    TopLevelId: categoryLookup.TryGetValue(entry.Key, out var category)
                        ? ResolveTopLevelCategory(category, categoryLookup)?.Id
                        : null,
                    entry.Value))
                .Where(entry => entry.TopLevelId.HasValue)
                .GroupBy(entry => entry.TopLevelId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Value));

            ChipCoECount = (coeCategory != null && countsByCategory.TryGetValue(coeCategory.Id, out var coeCount))
                ? coeCount
                : 0;
            ChipDcdCount = (dcdCategory != null && countsByCategory.TryGetValue(dcdCategory.Id, out var dcdCount))
                ? dcdCount
                : 0;
            ChipOtherRdCount = (otherCategory != null && countsByCategory.TryGetValue(otherCategory.Id, out var otherCount))
                ? otherCount
                : 0;

            // One chip per top-level category, so categories added in Admin get a chip too.
            CategoryChips = orderedCategories
                .Select(category => new CategoryChipVm(
                    category.Id,
                    OngoingCategoryFilter.ChipLabel(category.Name),
                    category.Name,
                    countsByCategory.TryGetValue(category.Id, out var count) ? count : 0,
                    SelectedCategoryIds.Contains(category.Id)))
                .ToList();

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

        // SECTION: Filter resolution shared by the page and the export
        private void ResolveFilters()
        {
            var buckets = Stage.Append(StageBucket)
                .Select(NormalizeStageBucket)
                .Where(bucket => bucket is not null)
                .Select(bucket => bucket!)
                .ToHashSet(StringComparer.Ordinal);
            SelectedStageBuckets = StageBucketOrder.Where(buckets.Contains).ToList();

            var nodes = _categories
                .Select(category => new OngoingCategoryNode(category.Id, category.Name, category.ParentId))
                .ToList();
            var categories = OngoingCategoryFilter.Resolve(nodes, Category, ProjectCategoryId, SubCategory);
            SelectedCategoryIds = categories.TopLevelIds;
            SubCategory = categories.SubCategoryId;
            EffectiveCategoryIds = categories.EffectiveIds;

            HasSubCategories = OngoingCategoryFilter.SubCategoryOptions(nodes, Array.Empty<int>()).Count > 0;
            SubCategoryOptions = new[] { new SelectListItem("Any sub-category", string.Empty, !SubCategory.HasValue) }
                .Concat(OngoingCategoryFilter.SubCategoryOptions(nodes, SelectedCategoryIds)
                    .Select(option => new SelectListItem(
                        option.Label,
                        option.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        option.Id == SubCategory)))
                .ToList();

            // The legacy single values are folded into the lists above and not echoed back.
            StageBucket = null;
            ProjectCategoryId = null;

            // An exact stage must sit inside the selected buckets, or it is dropped.
            PresentStageCode = NormalizeStageCode(PresentStageCode);
            LoadPresentStageOptions();
            PresentStageCode = ValidateStageCode(PresentStageCode);
        }

        public static string BucketKey(ProjectManagement.Models.Stages.StageBucket bucket) => bucket switch
        {
            ProjectManagement.Models.Stages.StageBucket.Approval => "approval",
            ProjectManagement.Models.Stages.StageBucket.Aon => "aon",
            ProjectManagement.Models.Stages.StageBucket.Procurement => "procurement",
            ProjectManagement.Models.Stages.StageBucket.Development => "development",
            _ => "unknown"
        };

        /// <summary>This page's address with the filters in effect, optionally for a handler or another view.</summary>
        public string FilterUrl(string? handler = null, string? view = null, string? sort = null, string? sortDir = null)
        {
            var query = new QueryBuilder();
            if (!string.IsNullOrWhiteSpace(handler)) query.Add("handler", handler);
            foreach (var bucket in SelectedStageBuckets) query.Add(nameof(Stage), bucket);
            foreach (var categoryId in SelectedCategoryIds) query.Add(nameof(Category), categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (SubCategory is { } subCategoryId) query.Add(nameof(SubCategory), subCategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(PresentStageCode)) query.Add(nameof(PresentStageCode), PresentStageCode);
            if (!string.IsNullOrWhiteSpace(ProjectOfficerId)) query.Add(nameof(ProjectOfficerId), ProjectOfficerId.Trim());
            if (!string.IsNullOrWhiteSpace(Search)) query.Add(nameof(Search), Search.Trim());
            var targetView = view ?? View ?? "timeline";
            query.Add(nameof(View), targetView);
            query.Add(nameof(StageFlow), StageFlow);
            // A column sort belongs to the table; switching to the timeline drops it.
            var sortKey = sort ?? (targetView == "table" ? Sort : null);
            if (sortKey is not null)
            {
                query.Add(nameof(Sort), sortKey);
                query.Add(nameof(SortDir), sortDir ?? SortDir ?? OngoingProjectSort.NormalizeDirection(sortKey, null));
            }

            return (Url.Page("./Index") ?? "/Projects/Ongoing") + query.ToQueryString();
        }

        // SECTION: Table column sort links
        public string SortUrl(string column)
            => FilterUrl(view: "table", sort: column, sortDir: OngoingProjectSort.NextDirection(column, Sort, SortDir));

        /// <summary>The header's aria-sort value: ascending, descending or none.</summary>
        public string AriaSort(string column)
            => !string.Equals(Sort, column, StringComparison.Ordinal)
                ? "none"
                : SortDir == OngoingProjectSort.Descending ? "descending" : "ascending";

        private void NormalizeSort()
        {
            // Only the table sorts by column; the timeline keeps its stage order.
            Sort = View == "table" ? OngoingProjectSort.NormalizeKey(Sort) : null;
            SortDir = Sort is null ? null : OngoingProjectSort.NormalizeDirection(Sort, SortDir);
        }

        // SECTION: Present stage normalization
        private static string? NormalizeStageCode(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

        // SECTION: Stage bucket normalization
        private static string? NormalizeStageBucket(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "approval" => "approval",
                "aon" => "aon",
                "procurement" => "procurement",
                "development" => "development",
                _ => null
            };
        }

        // SECTION: Present stage options
        private void LoadPresentStageOptions()
        {
            var stages = _workflowStageMetadataProvider.GetStages(null);

            var list = new List<SelectListItem>
            {
                new(SelectedStageBuckets.Count > 0 ? "Any stage in selection" : "Any stage", string.Empty, string.IsNullOrWhiteSpace(PresentStageCode))
            };

            foreach (var stage in stages.Where(stage => SelectedStageBuckets.Count == 0
                || SelectedStageBuckets.Contains(BucketKey(StageBuckets.Of(stage.Code)))))
            {
                list.Add(new SelectListItem(
                    stage.Name,
                    stage.Code,
                    string.Equals(stage.Code, PresentStageCode, StringComparison.OrdinalIgnoreCase)));
            }

            PresentStageOptions = list;
        }

        // SECTION: Present stage validation
        private string? ValidateStageCode(string? stageCode)
        {
            if (string.IsNullOrWhiteSpace(stageCode))
            {
                return null;
            }

            var allowed = PresentStageOptions
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => item.Value!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return allowed.Contains(stageCode) ? stageCode : null;
        }

        // SECTION: View normalization
        private static string NormalizeView(string? value)
        {
            if (string.Equals(value, "table", StringComparison.OrdinalIgnoreCase))
            {
                return "table";
            }

            return "timeline";
        }

        // SECTION: Stage flow normalization
        private static string NormalizeStageFlow(string? value)
            => OngoingStagePresentationPolicy.NormalizeStageFlow(value);

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
    public sealed record CategoryChipVm(int Id, string Label, string FullName, int Count, bool IsSelected);

    public sealed class CategoryCountDto
    {
        public int ProjectCategoryId { get; init; }
        public string CategoryName { get; init; } = "";
        public int Count { get; init; }
    }
}
