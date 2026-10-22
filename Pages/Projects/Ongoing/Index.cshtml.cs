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

        /// <summary>Stage buckets in effect: the chips plus any legacy single value, in pipeline order.</summary>
        public IReadOnlyList<string> SelectedStageBuckets { get; private set; } = Array.Empty<string>();

        /// <summary>Category ids in effect: the chips plus any legacy single value.</summary>
        public IReadOnlyList<int> SelectedCategoryIds { get; private set; } = Array.Empty<int>();

        public IReadOnlyDictionary<int, int> CategoryCounts { get; private set; } = new Dictionary<int, int>();
        public int AllProjectsCount { get; private set; }

        public bool HasFilters => SelectedStageBuckets.Count > 0
            || SelectedCategoryIds.Count > 0
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
            await LoadCategoriesAsync(cancellationToken);
            ResolveFilters();

            var officerId = Normalize(ProjectOfficerId);
            var search = Normalize(Search);

            ProjectOfficerOptions = await _ongoingService.GetProjectOfficerOptionsAsync(
                officerId,
                cancellationToken);

            Items = await _ongoingService.GetAsync(
                SelectedCategoryIds,
                officerId,
                search,
                PresentStageCode,
                SelectedStageBuckets,
                StageFlow,
                cancellationToken);

            // Each chip group counts with every filter except its own, so a count says
            // what selecting that chip would show.
            BucketCounts = await _ongoingService.GetStageBucketCountsAsync(
                SelectedCategoryIds,
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
            StageFlow = NormalizeStageFlow(StageFlow);
            await LoadCategoriesAsync(cancellationToken);
            ResolveFilters();
            var officerId = Normalize(ProjectOfficerId);
            var search = Normalize(Search);

            var items = await _ongoingService.GetAsync(
                SelectedCategoryIds,
                officerId,
                search,
                PresentStageCode,
                SelectedStageBuckets,
                StageFlow,
                cancellationToken);

            var now = _clock.UtcNow;

            var file = _excelBuilder.Build(
                new OngoingProjectsExportContext(
                    items,
                    now,
                    SelectedCategoryIds.Count == 1 ? SelectedCategoryIds[0] : null,
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

            var knownCategoryIds = _categories.Select(category => category.Id).ToHashSet();
            SelectedCategoryIds = Category
                .Append(ProjectCategoryId ?? 0)
                .Where(knownCategoryIds.Contains)
                .Distinct()
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
        public string FilterUrl(string? handler = null, string? view = null)
        {
            var query = new QueryBuilder();
            if (!string.IsNullOrWhiteSpace(handler)) query.Add("handler", handler);
            foreach (var bucket in SelectedStageBuckets) query.Add(nameof(Stage), bucket);
            foreach (var categoryId in SelectedCategoryIds) query.Add(nameof(Category), categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(PresentStageCode)) query.Add(nameof(PresentStageCode), PresentStageCode);
            if (!string.IsNullOrWhiteSpace(ProjectOfficerId)) query.Add(nameof(ProjectOfficerId), ProjectOfficerId.Trim());
            if (!string.IsNullOrWhiteSpace(Search)) query.Add(nameof(Search), Search.Trim());
            query.Add(nameof(View), view ?? View ?? "timeline");
            query.Add(nameof(StageFlow), StageFlow);

            return (Url.Page("./Index") ?? "/Projects/Ongoing") + query.ToQueryString();
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
    public sealed class CategoryCountDto
    {
        public int ProjectCategoryId { get; init; }
        public string CategoryName { get; init; } = "";
        public int Count { get; init; }
    }
}
