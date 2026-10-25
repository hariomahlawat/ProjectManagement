using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;

using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.Analytics
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IProjectAnalyticsService _projectAnalyticsService;
        private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
        private IReadOnlyDictionary<int, ProjectCategoryHierarchyNode> _categoryHierarchy =
            new Dictionary<int, ProjectCategoryHierarchyNode>();
        private CoeAnalyticsVm? _cachedCoeAnalytics;

        // SECTION: Analytics constants
        private const string DefaultCoeSubcategoryName = "Unspecified";
        private const int MaxCoeSubcategoryBuckets = 10;
        private const string UnassignedStageCode = "UNASSIGNED";
        private const string UnassignedStageName = "Unassigned";
        // END SECTION
        private static readonly string[] CoeCategoryKeywords =
        {
            "coe",
            "center of excellence",
            "centre of excellence",
            "centers of excellence",
            "centres of excellence"
        };

        private readonly IArppAnalyticsService? _arppAnalytics;

        public IndexModel(
            ApplicationDbContext db,
            IProjectAnalyticsService projectAnalyticsService,
            IWorkflowStageMetadataProvider workflowStageMetadataProvider,
            IArppAnalyticsService? arppAnalytics = null)
        {
            _db = db;
            _projectAnalyticsService = projectAnalyticsService;
            _workflowStageMetadataProvider = workflowStageMetadataProvider;
            _arppAnalytics = arppAnalytics;
        }

        private static readonly ProjectLifecycleFilter[] LifecycleFilters =
        {
            ProjectLifecycleFilter.Active,
            ProjectLifecycleFilter.Completed,
            ProjectLifecycleFilter.Cancelled,
            ProjectLifecycleFilter.All
        };

        public AnalyticsTab ActiveTab { get; private set; } = AnalyticsTab.Ongoing;

        [BindProperty(SupportsGet = true, Name = "categoryId")]
        public int? StageTimeCategoryId { get; set; }

        [BindProperty(SupportsGet = true, Name = "hotspotCategoryId")]
        public int? StageHotspotCategoryId { get; set; }

        [BindProperty(SupportsGet = true, Name = "financialYearStart")]
        public int? ArppFinancialYearStart { get; set; }

        public ArppAnalyticsVm? Arpp { get; private set; }

        [BindProperty(SupportsGet = true, Name = "ongoingStageParentCategoryIds")]
        public List<int> OngoingStageParentCategoryIds { get; set; } = new();

        public IReadOnlyList<CategoryOption> Categories { get; private set; } = Array.Empty<CategoryOption>();
        public IReadOnlyList<TechnicalCategoryOption> TechnicalCategories { get; private set; } = Array.Empty<TechnicalCategoryOption>();
        public IReadOnlyList<AnalyticsFilterOption> OngoingStageParentCategoryOptions { get; private set; } =
            Array.Empty<AnalyticsFilterOption>();
        public IReadOnlyList<int> ActiveOngoingStageParentCategoryIds { get; private set; } = Array.Empty<int>();

        public int CompletedCount { get; private set; }
        public int OngoingCount { get; private set; }
        public int CoeCount { get; private set; }

        public CompletedAnalyticsVm? Completed { get; private set; }
        public OngoingAnalyticsVm? Ongoing { get; private set; }
        public CoeAnalyticsVm? Coe { get; private set; }
        public StageTimeInsightsPanelVm? Insights { get; private set; }

        // SECTION: Page header and headline figures
        private static readonly TimeZoneInfo IndiaStandardTime = IstClock.TimeZone;

        /// <summary>When the figures were read, in IST; shown as the page's "as of" date.</summary>
        public DateTimeOffset AsOfIst { get; private set; }

        /// <summary>Three or four large figures that open the active mode, computed from data already loaded.</summary>
        public IReadOnlyList<HeadlineFigure> Headlines { get; private set; } = Array.Empty<HeadlineFigure>();

        /// <summary>Workflow stages in order, so every stage chart draws the same axis.</summary>
        public IReadOnlyList<WorkflowStageOption> WorkflowStages { get; } = StageCodes.All
            .Select(code => new WorkflowStageOption(code, StageCodes.DisplayNameOf(code)))
            .ToList();

        public sealed record HeadlineFigure(string Label, string Value, string? Hint = null, string? Href = null);
        public sealed record WorkflowStageOption(string Code, string Name);
        // END SECTION

        public ProjectLifecycleFilter DefaultLifecycle => ProjectLifecycleFilter.Active;

        public int LifecycleViewCount => LifecycleFilters.Length;

        public int CategoryCount => Categories.Count;

        public int TechnicalGroupCount => TechnicalCategories.Count;

        public async Task OnGetAsync(string? tab, CancellationToken cancellationToken)
        {
            ActiveTab = tab?.ToLowerInvariant() switch
            {
                "completed" => AnalyticsTab.Completed,
                "coe" => AnalyticsTab.Coe,
                "insights" => AnalyticsTab.Insights,
                "arpp" => AnalyticsTab.Arpp,
                _ => AnalyticsTab.Ongoing
            };

            AsOfIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IndiaStandardTime);
            await LoadAnalyticsAsync(cancellationToken);

            // SECTION: Active tab hydration
            switch (ActiveTab)
            {
                case AnalyticsTab.Completed:
                    Completed = await BuildCompletedAnalyticsAsync(cancellationToken);
                    CompletedCount = Completed.TotalCompletedProjects;
                    break;

                case AnalyticsTab.Ongoing:
                    Ongoing = await BuildOngoingAnalyticsAsync(cancellationToken);
                    OngoingCount = Ongoing.TotalOngoingProjects;
                    break;

                case AnalyticsTab.Coe:
                    Coe = _cachedCoeAnalytics ?? await BuildCoeAnalyticsAsync(cancellationToken);
                    break;

                case AnalyticsTab.Insights:
                {
                    // SECTION: Stage time insight loading
                    // One category filter drives the cycle-time chart and the hotspot list. The old
                    // hotspotCategoryId query key still works for bookmarked links.
                    var insightCategoryId = StageTimeCategoryId ?? StageHotspotCategoryId;
                    var stageCycleResult = await _projectAnalyticsService
                        .GetStageTimeInsightsAsync(insightCategoryId, cancellationToken);
                    // END SECTION

                    var ranked = RankStageHotspots(stageCycleResult.StageHotspots, out var portfolioMedian);

                    Insights = new StageTimeInsightsPanelVm
                    {
                        StageCycleTime = new StageTimeCycleChartVm
                        {
                            Rows = stageCycleResult.Rows,
                            SelectedCategoryId = insightCategoryId ?? stageCycleResult.SelectedCategoryId
                        },
                        StageHotspots = new StageHotspotChartVm
                        {
                            Points = stageCycleResult.StageHotspots,
                            SelectedCategoryId = insightCategoryId ?? stageCycleResult.SelectedCategoryId
                        },
                        SelectedCategoryId = insightCategoryId ?? stageCycleResult.SelectedCategoryId,
                        PortfolioMedianDays = portfolioMedian,
                        RankedHotspots = ranked
                    };
                    break;
                }

                case AnalyticsTab.Arpp:
                    // The ARPP rules (year choice, bands, standing, milestones) live in the service; the page only binds the year.
                    Arpp = _arppAnalytics is null
                        ? null
                        : await _arppAnalytics.BuildAsync(ArppFinancialYearStart, DateOnly.FromDateTime(AsOfIst.DateTime), cancellationToken);
                    break;
            }
            // END SECTION

            Headlines = BuildHeadlines();
        }

        // SECTION: Headline figures
        private IReadOnlyList<HeadlineFigure> BuildHeadlines()
        {
            var ongoingUrl = PageUrl("/Projects/Ongoing/Index");
            var completedUrl = PageUrl("/Projects/Index", new { Lifecycle = ProjectLifecycleFilter.Completed });

            switch (ActiveTab)
            {
                case AnalyticsTab.Ongoing when Ongoing is not null:
                    return new[]
                    {
                        new HeadlineFigure("Ongoing projects", Ongoing.TotalOngoingProjects.ToString(), "in the portfolio", ongoingUrl),
                        new HeadlineFigure(
                            "Busiest stage",
                            Ongoing.TopStageName ?? "—",
                            Ongoing.TopStageName is null ? null : $"{Ongoing.TopStageCount} {Plural(Ongoing.TopStageCount, "project")} there now"),
                        new HeadlineFigure(
                            "Median time in stage",
                            FormatDays(Ongoing.MedianDaysInCurrentStage),
                            "days in the current stage"),
                        new HeadlineFigure(
                            $"Past {OngoingAnalyticsVm.LongStageThresholdDays} days",
                            Ongoing.ProjectsPastThreshold.ToString(),
                            $"{Plural(Ongoing.ProjectsPastThreshold, "project")} in one stage longer than that")
                    };

                case AnalyticsTab.Completed when Completed is not null:
                    return new[]
                    {
                        new HeadlineFigure("Completed projects", Completed.TotalCompletedProjects.ToString(), "all time", completedUrl),
                        new HeadlineFigure(
                            "This year",
                            Completed.CompletedThisYear.ToString(),
                            $"completed in {AsOfIst.Year}",
                            PageUrl("/Projects/Index", new { Lifecycle = ProjectLifecycleFilter.Completed, CompletedYear = AsOfIst.Year })),
                        new HeadlineFigure(
                            "Best year",
                            Completed.BestYear?.ToString() ?? "—",
                            Completed.BestYear is null ? null : $"{Completed.BestYearCount} {Plural(Completed.BestYearCount, "project")} completed",
                            Completed.BestYear is null ? null : PageUrl("/Projects/Index", new { Lifecycle = ProjectLifecycleFilter.Completed, CompletedYear = Completed.BestYear })),
                        new HeadlineFigure(
                            "Median time to complete",
                            FormatYears(Completed.MedianDaysToComplete),
                            "from first stage start to completion")
                    };

                case AnalyticsTab.Coe when Coe is not null:
                    return new[]
                    {
                        new HeadlineFigure("CoE projects", Coe.TotalCoeProjects.ToString(), "all time"),
                        new HeadlineFigure("Ongoing", Coe.OngoingCount.ToString(), "CoE projects in progress"),
                        new HeadlineFigure("Completed", Coe.CompletedCount.ToString(), $"{Coe.CompletedThisYearCount} this year"),
                        new HeadlineFigure("Sub-categories", Coe.SubcategoryProjects.Count.ToString(), "with at least one project")
                    };

                case AnalyticsTab.Insights when Insights is not null:
                    var slowest = Insights.SlowestStage;
                    var fastest = Insights.FastestStage;
                    return new[]
                    {
                        new HeadlineFigure(
                            "Slowest stage",
                            slowest?.StageName ?? "—",
                            slowest is null ? null : $"{FormatDays(slowest.MedianDays)} {Plural((int)Math.Round(slowest.MedianDays), "day")} median"),
                        new HeadlineFigure(
                            "Fastest stage",
                            fastest?.StageName ?? "—",
                            fastest is null ? null : $"{FormatDays(fastest.MedianDays)} {Plural((int)Math.Round(fastest.MedianDays), "day")} median"),
                        new HeadlineFigure(
                            "Portfolio median",
                            FormatDays(Insights.RankedHotspots.Count > 0 ? Insights.PortfolioMedianDays : null),
                            "days per stage, across all stages"),
                        new HeadlineFigure(
                            "Stages measured",
                            Insights.RankedHotspots.Count.ToString(),
                            "with recorded start and end dates")
                    };

                case AnalyticsTab.Arpp when Arpp is not null && Arpp.HasData:
                    return new[]
                    {
                        new HeadlineFigure(
                            "Listed projects",
                            Arpp.ListedCount.ToString(),
                            $"IPA {IndianCurrencyFormatter.FormatCompact(Arpp.ListedIpaValue)} in FY {Arpp.FinancialYearDisplay}"),
                        new HeadlineFigure(
                            "New this year",
                            Arpp.NewCount.ToString(),
                            $"CL {Arpp.CommittedLiabilityCount} · CF {Arpp.CarryForwardCount} · delisted {Arpp.DelistedCount}"),
                        new HeadlineFigure(
                            "Completed",
                            Arpp.CompletedCount.ToString(),
                            $"of the listed projects; {Arpp.OngoingCount} ongoing"),
                        new HeadlineFigure(
                            "Supply orders placed",
                            Arpp.SupplyOrderCount.ToString(),
                            Arpp.SupplyOrderValue > 0
                                ? $"{IndianCurrencyFormatter.FormatCompact(Arpp.SupplyOrderValue)}, {Math.Round(Arpp.SupplyOrderShareOfIpa * 100):0}% of listed IPA"
                                : "no supply-order value recorded yet")
                    };

                default:
                    return Array.Empty<HeadlineFigure>();
            }
        }

        private static IReadOnlyList<StageHotspotRankVm> RankStageHotspots(
            IReadOnlyList<StageHotspotPointVm> points,
            out double portfolioMedian)
        {
            var measured = points
                .Where(point => point.ProjectCount > 0)
                .OrderByDescending(point => point.MedianDays)
                .ThenBy(point => point.StageOrder)
                .ToList();

            portfolioMedian = CalculateMedian(measured.Select(point => point.MedianDays).ToList());
            var slowestMedian = measured.Count > 0 ? measured[0].MedianDays : 0;
            var baseline = portfolioMedian;

            return measured
                .Select((point, index) => new StageHotspotRankVm(
                    Rank: index + 1,
                    StageKey: point.StageKey,
                    StageName: point.StageName,
                    MedianDays: point.MedianDays,
                    AverageDays: point.AverageDays,
                    ProjectCount: point.ProjectCount,
                    RatioToPortfolioMedian: baseline > 0 ? point.MedianDays / baseline : 0,
                    ShareOfSlowest: slowestMedian > 0 ? point.MedianDays / slowestMedian : 0))
                .ToList();
        }

        /// <summary>Url.Page needs a request; unit tests build the model without one, so headlines then carry no links.</summary>
        private string? PageUrl(string page, object? values = null) =>
            PageContext?.HttpContext is null ? null : (values is null ? Url.Page(page) : Url.Page(page, values));

        private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";

        private static string FormatDays(double? days) =>
            days.HasValue ? Math.Round(days.Value).ToString("0") : "—";

        private static string FormatYears(double? days)
        {
            if (!days.HasValue)
            {
                return "—";
            }

            var years = days.Value / 365.25;
            return years >= 1 ? $"{years:0.0} yr" : $"{Math.Round(days.Value):0} d";
        }
        // END SECTION

        private async Task LoadAnalyticsAsync(CancellationToken cancellationToken)
        {
            var categoryRows = await _db.ProjectCategories
                .AsNoTracking()
                .Select(c => new ProjectCategoryRow(
                    c.Id,
                    c.Name,
                    c.ParentId,
                    c.IsActive,
                    c.SortOrder))
                .ToListAsync(cancellationToken);

            Categories = categoryRows
                .OrderBy(c => c.Name)
                .Select(c => new CategoryOption(c.Id, c.Name))
                .ToList();

            _categoryHierarchy = categoryRows.ToDictionary(
                category => category.Id,
                category => new ProjectCategoryHierarchyNode(
                    category.Id,
                    category.Name,
                    category.ParentId));

            TechnicalCategories = await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new TechnicalCategoryOption(c.Id, c.Name))
                .ToListAsync(cancellationToken);

            OngoingStageParentCategoryOptions = categoryRows
                .Where(c => c.IsActive && c.ParentId == null)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new AnalyticsFilterOption(c.Id, c.Name))
                .ToList();

            ActiveOngoingStageParentCategoryIds = OngoingStageParentCategoryIds
                .Where(id => id > 0)
                .Distinct()
                .Where(id => OngoingStageParentCategoryOptions.Any(option => option.Id == id))
                .ToList();

            if (ActiveTab != AnalyticsTab.Completed)
            {
                CompletedCount = await _db.Projects
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed)
                    .CountAsync(cancellationToken);
            }

            OngoingCount = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active)
                .CountAsync(cancellationToken);

            _cachedCoeAnalytics = await BuildCoeAnalyticsAsync(cancellationToken);
            CoeCount = _cachedCoeAnalytics.TotalCoeProjects;
        }

        private async Task<CompletedAnalyticsVm> BuildCompletedAnalyticsAsync(CancellationToken cancellationToken)
        {
            // SECTION: Completed analytics aggregation
            var completedQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

            var byCategory = await BuildCategoryCountsAsync(completedQuery, cancellationToken);

            var byTechnical = await BuildTechnicalCategoryCountsAsync(completedQuery, cancellationToken);

            var perYearByParentCategory = await BuildCompletedPerYearByParentCategoryAsync(
                completedQuery,
                cancellationToken);
            var yearBoard = await BuildCompletedYearBoardAsync(completedQuery, cancellationToken);

            // SECTION: Completed per-year aggregation
            var completionDates = await completedQuery
                .Select(p => new { p.CompletedYear, p.CompletedOn })
                .ToListAsync(cancellationToken);

            var perYear = completionDates
                .Select(p => p.CompletedYear ?? (p.CompletedOn.HasValue ? p.CompletedOn.Value.Year : (int?)null))
                .Where(year => year.HasValue)
                .GroupBy(year => year!.Value)
                .Select(g => new CompletedPerYearPoint(g.Key, g.Count()))
                .OrderBy(x => x.Year)
                .ToList();
            // END SECTION

            // SECTION: Headline figures (this year, best year, median time to complete)
            var currentYear = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IndiaStandardTime).Year;
            var bestYear = perYear.OrderByDescending(point => point.Count).ThenByDescending(point => point.Year).FirstOrDefault();
            var medianDaysToComplete = await BuildMedianDaysToCompleteAsync(completedQuery, cancellationToken);
            var (categoryBoard, technicalBoard) = await BuildCompletedGroupBoardsAsync(completedQuery, cancellationToken);
            // END SECTION

            return new CompletedAnalyticsVm
            {
                ByCategory = byCategory,
                ByTechnical = byTechnical,
                PerYear = perYear,
                PerYearByParentCategory = perYearByParentCategory,
                YearBoard = yearBoard,
                TotalCompletedProjects = await completedQuery.CountAsync(cancellationToken),
                CompletedThisYear = perYear.FirstOrDefault(point => point.Year == currentYear)?.Count ?? 0,
                BestYear = bestYear?.Year,
                BestYearCount = bestYear?.Count ?? 0,
                MedianDaysToComplete = medianDaysToComplete,
                CategoryBoard = categoryBoard,
                TechnicalBoard = technicalBoard
            };
            // END SECTION
        }

        // SECTION: Completed drill-downs by exact category and technical category (same grouping as the charts)
        private async Task<(IReadOnlyList<AnalyticsBoardGroupVm> CategoryBoard, IReadOnlyList<AnalyticsBoardGroupVm> TechnicalBoard)>
            BuildCompletedGroupBoardsAsync(IQueryable<Project> completedQuery, CancellationToken cancellationToken)
        {
            var rows = await completedQuery
                .Select(project => new CompletedBoardRow(
                    project.Id,
                    project.Name,
                    project.Category != null ? project.Category.Name : null,
                    project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                    project.CompletedYear ?? (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : (int?)null)))
                .ToListAsync(cancellationToken);

            IReadOnlyList<AnalyticsBoardGroupVm> Group(Func<CompletedBoardRow, string?> keyOf) => rows
                .GroupBy(row => string.IsNullOrWhiteSpace(keyOf(row)) ? "Uncategorized" : keyOf(row)!.Trim())
                .Select(group => new AnalyticsBoardGroupVm(
                    group.Key,
                    group.Key,
                    group.Count(),
                    group
                        .OrderByDescending(row => row.Year)
                        .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(row => new AnalyticsBoardProjectVm(
                            row.Id,
                            string.IsNullOrWhiteSpace(row.Name) ? "Untitled project" : row.Name.Trim(),
                            row.Year?.ToString()))
                        .ToList()))
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (Group(row => row.CategoryName), Group(row => row.TechnicalName));
        }
        // END SECTION

        // SECTION: Median time to complete (first stage start to completion)
        private async Task<double?> BuildMedianDaysToCompleteAsync(
            IQueryable<Project> completedQuery,
            CancellationToken cancellationToken)
        {
            var rows = await completedQuery
                .Where(project => project.CompletedOn.HasValue)
                .Select(project => new
                {
                    project.CompletedOn,
                    FirstStart = project.ProjectStages
                        .Where(stage => stage.ActualStart.HasValue)
                        .Min(stage => stage.ActualStart)
                })
                .ToListAsync(cancellationToken);

            var spans = rows
                .Where(row => row.FirstStart.HasValue && row.CompletedOn.HasValue && row.CompletedOn.Value >= row.FirstStart.Value)
                .Select(row => (double)StageDurationCalculator.InclusiveCalendarDays(row.FirstStart!.Value, row.CompletedOn!.Value))
                .ToList();

            return spans.Count == 0 ? null : CalculateMedian(spans);
        }
        // END SECTION

        private async Task<OngoingAnalyticsVm> BuildOngoingAnalyticsAsync(CancellationToken cancellationToken)
        {
            // SECTION: Ongoing analytics aggregation
            var ongoingQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active);

            var total = await ongoingQuery.CountAsync(cancellationToken);
            var byStage = await BuildOngoingStageDistributionAsync(cancellationToken);
            var byStageByParentCategory = await BuildOngoingStageDistributionByParentCategoryAsync(
                ActiveOngoingStageParentCategoryIds,
                cancellationToken);
            var stageBoard = await BuildOngoingStageBoardAsync(
                ActiveOngoingStageParentCategoryIds,
                OngoingStageParentCategoryOptions,
                cancellationToken);
            var stageDurations = await BuildOngoingStageDurationsAsync(cancellationToken);

            // SECTION: Headline figures from the (filtered) stage rows
            var stageRows = await BuildOngoingStageRowsAsync(ActiveOngoingStageParentCategoryIds, cancellationToken);
            var topStage = stageBoard.OrderByDescending(item => item.StageCount).FirstOrDefault();
            var stageAges = stageRows
                .Where(row => row.DaysInStage.HasValue)
                .Select(row => (double)row.DaysInStage!.Value)
                .ToList();
            // END SECTION

            return new OngoingAnalyticsVm
            {
                TotalOngoingProjects = total,
                ByStage = byStage,
                ByStageByParentCategory = byStageByParentCategory,
                StageBoard = stageBoard,
                AvgStageDurations = stageDurations,
                TopStageName = topStage?.StageName,
                TopStageCount = topStage?.StageCount ?? 0,
                MedianDaysInCurrentStage = stageAges.Count == 0 ? null : CalculateMedian(stageAges),
                ProjectsPastThreshold = stageAges.Count(days => days > OngoingAnalyticsVm.LongStageThresholdDays)
            };
            // END SECTION
        }

        internal async Task<CoeAnalyticsVm> BuildCoeAnalyticsAsync(CancellationToken cancellationToken)
        {
            // SECTION: CoE analytics aggregation
            var coeCategories = await LoadCoeCategoriesAsync(cancellationToken);
            if (coeCategories.CategoryIds.Count == 0)
            {
                return BuildEmptyCoeAnalyticsVm();
            }

            var coeCategoryIds = coeCategories.CategoryIds.ToList();

            var coeProjectsQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted
                    && !p.IsArchived
                    && p.CategoryId.HasValue
                    && coeCategoryIds.Contains(p.CategoryId.Value));

            var totalCoeProjects = await coeProjectsQuery.CountAsync(cancellationToken);
            if (totalCoeProjects == 0)
            {
                return BuildEmptyCoeAnalyticsVm();
            }

            var stageBuckets = await BuildCoeStageBucketsAsync(coeCategoryIds, cancellationToken);
            var subcategoryBreakdown = await BuildCoeSubcategoryBreakdownAsync(
                coeProjectsQuery,
                cancellationToken);
            var subcategoryProjects = await BuildCoeSubcategoryProjectsAsync(
                coeProjectsQuery,
                cancellationToken);

            // SECTION: Headline figures
            var currentYear = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IndiaStandardTime).Year;
            var ongoingCount = await coeProjectsQuery.CountAsync(p => p.LifecycleStatus == ProjectLifecycleStatus.Active, cancellationToken);
            var completedCount = await coeProjectsQuery.CountAsync(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed, cancellationToken);
            var completedThisYear = await coeProjectsQuery.CountAsync(
                p => p.LifecycleStatus == ProjectLifecycleStatus.Completed
                    && (p.CompletedYear ?? (p.CompletedOn.HasValue ? p.CompletedOn.Value.Year : (int?)null)) == currentYear,
                cancellationToken);
            // END SECTION

            return new CoeAnalyticsVm
            {
                ByStage = stageBuckets,
                SubcategoriesByLifecycle = subcategoryBreakdown,
                SubcategoryProjects = subcategoryProjects,
                StageBoard = BuildCoeStageBoard(subcategoryProjects),
                TotalCoeProjects = totalCoeProjects,
                OngoingCount = ongoingCount,
                CompletedCount = completedCount,
                CompletedThisYearCount = completedThisYear
            };
            // END SECTION
        }

        private async Task<IReadOnlyList<CoeStageBucketVm>> BuildCoeStageBucketsAsync(
            IReadOnlyCollection<int> coeCategoryIds,
            CancellationToken cancellationToken)
        {
            // SECTION: CoE stage distribution
            if (coeCategoryIds.Count == 0)
            {
                return Array.Empty<CoeStageBucketVm>();
            }

            var stageSnapshots = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted
                    && !p.IsArchived
                    && p.LifecycleStatus == ProjectLifecycleStatus.Active
                    && p.CategoryId.HasValue
                    && coeCategoryIds.Contains(p.CategoryId.Value))
                .Select(p => new ProjectStageSnapshot(
                    p.LifecycleStatus,
                    p.WorkflowVersion,
                    p.ProjectStages
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.StageCode)
                        .Select(s => new StageSnapshot(
                            s.StageCode,
                            s.Status,
                            s.SortOrder,
                            s.ActualStart,
                            s.CompletedOn))
                        .ToList()))
                .ToListAsync(cancellationToken);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in stageSnapshots)
            {
                var stage = ResolvePresentStage(project);
                var stageCode = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                    ? UnassignedStageCode
                    : stage.CurrentStageCode.Trim();

                counts.TryGetValue(stageCode, out var existing);
                counts[stageCode] = existing + 1;
            }

            var orderedCodes = StageCodes.All
                .Concat(counts.Keys.Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return orderedCodes
                .Select(code =>
                {
                    counts.TryGetValue(code, out var count);
                    return new CoeStageBucketVm(
                        StageKey: code,
                        StageName: StageCodes.DisplayNameOf(code),
                        ProjectCount: count);
                })
                .ToList();
            // END SECTION
        }

        private async Task<IReadOnlyList<CoeSubcategoryLifecycleVm>> BuildCoeSubcategoryBreakdownAsync(
            IQueryable<Project> coeProjectsQuery,
            CancellationToken cancellationToken)
        {
            // SECTION: CoE sub-category aggregation
            var lifecycleSnapshots = await coeProjectsQuery
                .Select(p => new
                {
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    p.LifecycleStatus
                })
                .ToListAsync(cancellationToken);

            var groupedBuckets = lifecycleSnapshots
                .Select(item => new
                {
                    Subcategory = NormalizeCoeSubcategoryName(item.CategoryName),
                    item.LifecycleStatus
                })
                .GroupBy(item => item.Subcategory)
                .Select(g => new
                {
                    Subcategory = g.Key,
                    Ongoing = g.Count(item => item.LifecycleStatus == ProjectLifecycleStatus.Active),
                    Completed = g.Count(item => item.LifecycleStatus == ProjectLifecycleStatus.Completed),
                    Cancelled = g.Count(item => item.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
                })
                .ToList();

            if (groupedBuckets.Count == 0)
            {
                return Array.Empty<CoeSubcategoryLifecycleVm>();
            }

            var orderedBuckets = groupedBuckets
                .Select(bucket => new
                {
                    bucket.Subcategory,
                    bucket.Ongoing,
                    bucket.Completed,
                    bucket.Cancelled,
                    Total = bucket.Ongoing + bucket.Completed + bucket.Cancelled
                })
                .OrderByDescending(bucket => bucket.Total)
                .ThenBy(bucket => bucket.Subcategory, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var primaryBuckets = orderedBuckets
                .Take(MaxCoeSubcategoryBuckets)
                .Select(bucket => new CoeSubcategoryLifecycleVm(
                    bucket.Subcategory,
                    bucket.Ongoing,
                    bucket.Completed,
                    bucket.Cancelled,
                    bucket.Total))
                .ToList();

            var overflow = orderedBuckets.Skip(MaxCoeSubcategoryBuckets).ToList();
            if (overflow.Count > 0)
            {
                primaryBuckets.Add(new CoeSubcategoryLifecycleVm(
                    "Other",
                    overflow.Sum(bucket => bucket.Ongoing),
                    overflow.Sum(bucket => bucket.Completed),
                    overflow.Sum(bucket => bucket.Cancelled),
                    overflow.Sum(bucket => bucket.Total)));
            }

            return primaryBuckets;
            // END SECTION
        }

        private async Task<CoeCategoryLookup> LoadCoeCategoriesAsync(CancellationToken cancellationToken)
        {
            // SECTION: CoE category resolution
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .Select(c => new CoeCategoryDescriptor(c.Id, c.ParentId, c.Name))
                .ToListAsync(cancellationToken);

            if (categories.Count == 0)
            {
                return new CoeCategoryLookup(Array.Empty<int>(), new Dictionary<int, string>());
            }

            var descriptorLookup = categories.ToDictionary(c => c.Id);
            var matches = new HashSet<int>();

            foreach (var descriptor in categories)
            {
                if (IsCoeCategory(descriptor, descriptorLookup))
                {
                    matches.Add(descriptor.Id);
                }
            }

            var ids = matches.ToList();
            var names = categories
                .Where(c => matches.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.Name);

            return new CoeCategoryLookup(ids, names);
            // END SECTION
        }

        private static bool IsCoeCategory(
            CoeCategoryDescriptor descriptor,
            IReadOnlyDictionary<int, CoeCategoryDescriptor> lookup)
        {
            // SECTION: CoE category detector
            var current = descriptor;
            while (current is not null)
            {
                if (MatchesCoeName(current.Name))
                {
                    return true;
                }

                if (!current.ParentId.HasValue || !lookup.TryGetValue(current.ParentId.Value, out current))
                {
                    current = null;
                }
            }

            return false;
            // END SECTION
        }

        private static bool MatchesCoeName(string? name)
        {
            // SECTION: CoE keyword match
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return CoeCategoryKeywords.Any(keyword =>
                name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            // END SECTION
        }

        private static CoeAnalyticsVm BuildEmptyCoeAnalyticsVm()
        {
            // SECTION: Empty CoE analytics fallback
            return new CoeAnalyticsVm
            {
                ByStage = Array.Empty<CoeStageBucketVm>(),
                SubcategoriesByLifecycle = Array.Empty<CoeSubcategoryLifecycleVm>(),
                SubcategoryProjects = Array.Empty<CoeSubcategoryProjectsVm>(),
                TotalCoeProjects = 0
            };
            // END SECTION
        }

        private static string NormalizeCoeSubcategoryName(string? name)
        {
            // SECTION: CoE sub-category normaliser
            return string.IsNullOrWhiteSpace(name)
                ? DefaultCoeSubcategoryName
                : name.Trim();
            // END SECTION
        }

        private async Task<IReadOnlyList<CoeSubcategoryProjectsVm>> BuildCoeSubcategoryProjectsAsync(
            IQueryable<Project> coeProjectsQuery,
            CancellationToken cancellationToken)
        {
            // SECTION: CoE sub-category project listing aggregation
            var projectRows = await coeProjectsQuery
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    p.LifecycleStatus,
                    p.WorkflowVersion,
                    Stages = p.ProjectStages
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.StageCode)
                        .Select(s => new StageSnapshot(
                            s.StageCode,
                            s.Status,
                            s.SortOrder,
                            s.ActualStart,
                            s.CompletedOn))
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            if (projectRows.Count == 0)
            {
                return Array.Empty<CoeSubcategoryProjectsVm>();
            }

            var groupedProjects = projectRows
                .Select(row =>
                {
                    var stage = ResolvePresentStage(new ProjectStageSnapshot(
                        row.LifecycleStatus,
                        row.WorkflowVersion,
                        row.Stages));
                    var stageName = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                        ? "—"
                        : stage.CurrentStageName ?? StageCodes.DisplayNameOf(stage.CurrentStageCode);

                    return new
                    {
                        Subcategory = NormalizeCoeSubcategoryName(row.CategoryName),
                        row.LifecycleStatus,
                        Project = new CoeProjectSummaryVm(
                            row.Id,
                            row.Name,
                            FormatCoeLifecycleStatus(row.LifecycleStatus),
                            stageName,
                            string.IsNullOrWhiteSpace(stage.CurrentStageCode) ? null : stage.CurrentStageCode.Trim())
                    };
                })
                .GroupBy(item => item.Subcategory, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CoeSubcategoryProjectsVm(
                    group.Key,
                    group.Select(item => item.Project)
                        .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    OngoingCount: group.Count(item => item.LifecycleStatus == ProjectLifecycleStatus.Active),
                    CompletedCount: group.Count(item => item.LifecycleStatus == ProjectLifecycleStatus.Completed),
                    CancelledCount: group.Count(item => item.LifecycleStatus == ProjectLifecycleStatus.Cancelled)))
                .OrderBy(bucket => bucket.SubcategoryName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return groupedProjects;
            // END SECTION
        }

        // SECTION: CoE stage drill-down (ongoing projects grouped by current stage, workflow order)
        private static IReadOnlyList<AnalyticsBoardGroupVm> BuildCoeStageBoard(IReadOnlyList<CoeSubcategoryProjectsVm> buckets)
        {
            var ongoing = buckets
                .SelectMany(bucket => bucket.Projects.Select(project => (bucket.SubcategoryName, Project: project)))
                .Where(item => item.Project.LifecycleStatus == "Ongoing" && !string.IsNullOrWhiteSpace(item.Project.CurrentStageCode))
                .ToList();

            var order = BuildStageOrderLookup(BuildOrderedStageCodes(ongoing.Select(item => item.Project.CurrentStageCode!)));

            return ongoing
                .GroupBy(item => item.Project.CurrentStageCode!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => ResolveStageOrder(group.Key, order))
                .Select(group => new AnalyticsBoardGroupVm(
                    group.Key,
                    group.First().Project.CurrentStage,
                    group.Count(),
                    group
                        .OrderBy(item => item.Project.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(item => new AnalyticsBoardProjectVm(item.Project.Id, item.Project.Name, item.SubcategoryName))
                        .ToList()))
                .ToList();
        }
        // END SECTION

        private static string FormatCoeLifecycleStatus(ProjectLifecycleStatus status) => status switch
        {
            ProjectLifecycleStatus.Active => "Ongoing",
            ProjectLifecycleStatus.Completed => "Completed",
            ProjectLifecycleStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };

        private sealed record CompletedBoardRow(int Id, string? Name, string? CategoryName, string? TechnicalName, int? Year);

        private sealed record CoeCategoryDescriptor(int Id, int? ParentId, string Name);
        private sealed record CoeCategoryLookup(
            IReadOnlyCollection<int> CategoryIds,
            IReadOnlyDictionary<int, string> CategoryNames);

        // SECTION: Completed analytics helpers
        private async Task<IReadOnlyList<AnalyticsCategoryCountPoint>> BuildCategoryCountsAsync(
            IQueryable<Project> projectQuery,
            CancellationToken cancellationToken)
        {
            var categoryCounts = await projectQuery
                .GroupBy(p => p.CategoryId)
                .Select(g => new CategoryAggregation
                {
                    Id = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync(cancellationToken);

            var namedCategories = await LoadCategoryNamesAsync(categoryCounts, cancellationToken);

            return categoryCounts
                .Select(item => new AnalyticsCategoryCountPoint(ResolveName(item.Id, namedCategories), item.Count))
                .ToList();
        }

        private async Task<IReadOnlyList<AnalyticsCategoryCountPoint>> BuildParentCategoryCountsAsync(
            IQueryable<Project> projectQuery,
            CancellationToken cancellationToken)
        {
            // SECTION: Root category aggregation
            var categoryIds = await projectQuery
                .Select(project => project.CategoryId)
                .ToListAsync(cancellationToken);

            return categoryIds
                .Select(categoryId => ProjectCategoryHierarchyResolver.ResolveRoot(categoryId, _categoryHierarchy))
                .GroupBy(root => root?.Id)
                .Select(group => new AnalyticsCategoryCountPoint(
                    group.FirstOrDefault()?.Name ?? "Uncategorized",
                    group.Count()))
                .OrderByDescending(point => point.Count)
                .ThenBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // END SECTION
        }

        private async Task<IReadOnlyList<AnalyticsCategoryCountPoint>> BuildTechnicalCategoryCountsAsync(
            IQueryable<Project> projectQuery,
            CancellationToken cancellationToken)
        {
            var technicalCounts = await projectQuery
                .GroupBy(p => p.TechnicalCategoryId)
                .Select(g => new CategoryAggregation
                {
                    Id = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync(cancellationToken);

            var namedTechnicalCategories = await LoadTechnicalCategoryNamesAsync(technicalCounts, cancellationToken);

            return technicalCounts
                .Select(item => new AnalyticsCategoryCountPoint(ResolveName(item.Id, namedTechnicalCategories), item.Count))
                .ToList();
        }

        private async Task<IReadOnlyList<CompletedPerYearByParentCategoryPoint>> BuildCompletedPerYearByParentCategoryAsync(
            IQueryable<Project> completedQuery,
            CancellationToken cancellationToken)
        {
            var rows = await completedQuery
                .Where(project => project.CompletedYear.HasValue || project.CompletedOn.HasValue)
                .Select(project => new
                {
                    Year = project.CompletedYear ??
                        (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : (int?)null),
                    project.CategoryId
                })
                .Where(row => row.Year.HasValue)
                .ToListAsync(cancellationToken);

            return rows
                .Select(row => new
                {
                    Year = row.Year!.Value,
                    RootCategory = ProjectCategoryHierarchyResolver.ResolveRoot(row.CategoryId, _categoryHierarchy)
                })
                .GroupBy(row => new
                {
                    row.Year,
                    CategoryId = row.RootCategory?.Id,
                    CategoryName = row.RootCategory?.Name ?? "Uncategorized"
                })
                .Select(group => new CompletedPerYearByParentCategoryPoint(
                    group.Key.Year,
                    group.Key.CategoryName,
                    group.Count()))
                .OrderBy(point => point.Year)
                .ThenBy(point => point.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // SECTION: Completed analytics year board aggregation
        private async Task<IReadOnlyList<CompletedYearBoardItemVm>> BuildCompletedYearBoardAsync(
            IQueryable<Project> completedQuery,
            CancellationToken cancellationToken)
        {
            var rows = await completedQuery
                .Select(project => new
                {
                    project.Id,
                    project.Name,
                    project.CompletedOn,
                    project.CompletedYear,
                    project.CategoryId
                })
                .ToListAsync(cancellationToken);

            var shapedRows = rows
                .Select(row => new
                {
                    row.Id,
                    ProjectName = string.IsNullOrWhiteSpace(row.Name)
                        ? "Untitled project"
                        : row.Name.Trim(),
                    row.CompletedOn,
                    EffectiveYear = row.CompletedYear ?? row.CompletedOn?.Year,
                    ParentCategoryName = ProjectCategoryHierarchyResolver.ResolveRoot(
                            row.CategoryId,
                            _categoryHierarchy)?.Name
                        ?? "Unassigned"
                })
                .Where(row => row.EffectiveYear.HasValue)
                .ToList();

            return shapedRows
                .GroupBy(row => row.EffectiveYear!.Value)
                .OrderBy(group => group.Key)
                .Select(yearGroup => new CompletedYearBoardItemVm(
                    yearGroup.Key,
                    yearGroup.Count(),
                    yearGroup
                        .GroupBy(project => project.ParentCategoryName)
                        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(categoryGroup => new CompletedYearBoardCategoryVm(
                            categoryGroup.Key,
                            categoryGroup.Count(),
                            categoryGroup
                                .OrderByDescending(project => project.CompletedOn.HasValue)
                                .ThenByDescending(project => project.CompletedOn)
                                .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
                                .Select(project => new CompletedYearBoardProjectVm(
                                    project.Id,
                                    project.ProjectName,
                                    project.CompletedOn))
                                .ToList()))
                        .ToList()))
                .ToList();
        }
        // END SECTION

        private async Task<IReadOnlyDictionary<int, string>> LoadCategoryNamesAsync(
            IEnumerable<CategoryAggregation> aggregations,
            CancellationToken cancellationToken)
        {
            var ids = aggregations
                .Where(a => a.Id.HasValue)
                .Select(a => a.Id!.Value)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            return await _db.ProjectCategories
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        }

        private async Task<IReadOnlyDictionary<int, string>> LoadTechnicalCategoryNamesAsync(
            IEnumerable<CategoryAggregation> aggregations,
            CancellationToken cancellationToken)
        {
            var ids = aggregations
                .Where(a => a.Id.HasValue)
                .Select(a => a.Id!.Value)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            return await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        }

        private async Task<IReadOnlyList<AnalyticsStageCountPoint>> BuildOngoingStageDistributionAsync(
            CancellationToken cancellationToken)
        {
            var stageSnapshots = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active)
                .Include(p => p.ProjectStages)
                .Select(p => new ProjectStageSnapshot(
                    p.LifecycleStatus,
                    p.WorkflowVersion,
                    p.ProjectStages
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.StageCode)
                        .Select(s => new StageSnapshot(
                            s.StageCode,
                            s.Status,
                            s.SortOrder,
                            s.ActualStart,
                            s.CompletedOn))
                        .ToList()))
                .ToListAsync(cancellationToken);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in stageSnapshots)
            {
                var stage = ResolvePresentStage(project);
                var stageCode = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                    ? UnassignedStageCode
                    : stage.CurrentStageCode.Trim();

                counts.TryGetValue(stageCode, out var existing);
                counts[stageCode] = existing + 1;
            }

            var orderedCodes = StageCodes.All
                .Where(code => counts.ContainsKey(code))
                .Concat(counts.Keys.Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return orderedCodes
                .Select(code =>
                    new AnalyticsStageCountPoint(
                        string.Equals(code, UnassignedStageCode, StringComparison.OrdinalIgnoreCase)
                            ? UnassignedStageName
                            : StageCodes.DisplayNameOf(code),
                        counts[code]))
                .ToList();
        }

        private async Task<IReadOnlyList<OngoingStageByParentCategoryPoint>>
            BuildOngoingStageDistributionByParentCategoryAsync(
                IReadOnlyCollection<int>? selectedParentCategoryIds,
                CancellationToken cancellationToken)
        {
            // SECTION: Ongoing stage distribution by parent category
            var stageRows = await BuildOngoingStageRowsAsync(selectedParentCategoryIds, cancellationToken);
            if (stageRows.Count == 0)
            {
                return Array.Empty<OngoingStageByParentCategoryPoint>();
            }

            var orderedStageCodes = BuildOrderedStageCodes(stageRows.Select(row => row.StageCode));
            var stageOrderLookup = BuildStageOrderLookup(orderedStageCodes);

            return stageRows
                .GroupBy(row => new { row.StageCode, row.StageName, row.ParentCategoryName })
                .Select(group => new OngoingStageByParentCategoryPoint(
                    StageCode: group.Key.StageCode,
                    StageName: group.Key.StageName,
                    CategoryName: group.Key.ParentCategoryName,
                    Count: group.Count()))
                .OrderBy(point => ResolveStageOrder(point.StageCode, stageOrderLookup))
                .ThenBy(point => point.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // END SECTION
        }

        private async Task<IReadOnlyList<OngoingStageBoardItemVm>> BuildOngoingStageBoardAsync(
            IReadOnlyCollection<int>? selectedParentCategoryIds,
            IReadOnlyList<AnalyticsFilterOption> orderedParentCategories,
            CancellationToken cancellationToken)
        {
            // SECTION: Ongoing stage drill-down board
            var stageRows = await BuildOngoingStageRowsAsync(selectedParentCategoryIds, cancellationToken);
            if (stageRows.Count == 0)
            {
                return Array.Empty<OngoingStageBoardItemVm>();
            }

            var orderedStageCodes = BuildOrderedStageCodes(stageRows.Select(row => row.StageCode));
            var stageOrderLookup = BuildStageOrderLookup(orderedStageCodes);
            var parentCategoryOrder = orderedParentCategories
                .Select((option, index) => new { option.Id, Order = index })
                .ToDictionary(x => x.Id, x => x.Order);

            return stageRows
                .GroupBy(row => new { row.StageCode, row.StageName })
                .OrderBy(group => ResolveStageOrder(group.Key.StageCode, stageOrderLookup))
                .Select(stageGroup =>
                {
                    var categories = stageGroup
                        .GroupBy(row => new { row.ParentCategoryId, row.ParentCategoryName })
                        .OrderBy(group =>
                        {
                            if (group.Key.ParentCategoryId.HasValue &&
                                parentCategoryOrder.TryGetValue(group.Key.ParentCategoryId.Value, out var categoryOrder))
                            {
                                return categoryOrder;
                            }

                            return int.MaxValue;
                        })
                        .ThenBy(group => group.Key.ParentCategoryName, StringComparer.OrdinalIgnoreCase)
                        .Select(categoryGroup =>
                        {
                            var projects = categoryGroup
                                .OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(row => row.ProjectId)
                                .Select(row => new OngoingStageBoardProjectVm(row.ProjectId, row.ProjectName))
                                .ToList();

                            return new OngoingStageBoardCategoryVm(
                                ParentCategoryId: categoryGroup.Key.ParentCategoryId,
                                ParentCategoryName: categoryGroup.Key.ParentCategoryName,
                                CategoryCount: projects.Count,
                                Projects: projects);
                        })
                        .ToList();

                    return new OngoingStageBoardItemVm(
                        StageCode: stageGroup.Key.StageCode,
                        StageName: stageGroup.Key.StageName,
                        StageCount: stageGroup.Count(),
                        Categories: categories);
                })
                .ToList();
            // END SECTION
        }

        private static IReadOnlyDictionary<string, int> BuildStageOrderLookup(IReadOnlyList<string> orderedStageCodes)
        {
            // SECTION: Stage ordering lookup helper
            var stageOrderLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < orderedStageCodes.Count; index++)
            {
                stageOrderLookup[orderedStageCodes[index]] = index;
            }

            return stageOrderLookup;
            // END SECTION
        }

        private static int ResolveStageOrder(
            string stageCode,
            IReadOnlyDictionary<string, int> stageOrderLookup)
        {
            // SECTION: Stage ordering resolver
            return stageOrderLookup.TryGetValue(stageCode, out var orderIndex)
                ? orderIndex
                : int.MaxValue;
            // END SECTION
        }

        private async Task<IReadOnlyList<OngoingStageRow>> BuildOngoingStageRowsAsync(
            IReadOnlyCollection<int>? selectedParentCategoryIds,
            CancellationToken cancellationToken)
        {
            // SECTION: Ongoing stage row projection
            var selectedIds = selectedParentCategoryIds?
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet() ?? new HashSet<int>();

            var stageSnapshots = await _db.Projects
                .AsNoTracking()
                .Where(project =>
                    !project.IsDeleted
                    && !project.IsArchived
                    && project.LifecycleStatus == ProjectLifecycleStatus.Active)
                .Select(project => new
                {
                    project.Id,
                    project.Name,
                    project.LifecycleStatus,
                    project.WorkflowVersion,
                    project.CategoryId,
                    Stages = project.ProjectStages
                        .OrderBy(stage => stage.SortOrder)
                        .ThenBy(stage => stage.StageCode)
                        .Select(stage => new StageSnapshot(
                            stage.StageCode,
                            stage.Status,
                            stage.SortOrder,
                            stage.ActualStart,
                            stage.CompletedOn))
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            if (stageSnapshots.Count == 0)
            {
                return Array.Empty<OngoingStageRow>();
            }

            var rows = stageSnapshots
                .Select(snapshot =>
                {
                    var rootCategory = ProjectCategoryHierarchyResolver.ResolveRoot(
                        snapshot.CategoryId,
                        _categoryHierarchy);
                    var stage = ResolvePresentStage(new ProjectStageSnapshot(
                        snapshot.LifecycleStatus,
                        snapshot.WorkflowVersion,
                        snapshot.Stages));
                    var stageCode = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                        ? UnassignedStageCode
                        : stage.CurrentStageCode.Trim();
                    var stageName = string.Equals(
                            stageCode,
                            UnassignedStageCode,
                            StringComparison.OrdinalIgnoreCase)
                        ? UnassignedStageName
                        : stage.CurrentStageName ?? StageCodes.DisplayNameOf(stageCode);

                    return new OngoingStageRow(
                        ProjectId: snapshot.Id,
                        ProjectName: snapshot.Name ?? "Untitled project",
                        StageCode: stageCode,
                        StageName: stageName,
                        ParentCategoryId: rootCategory?.Id,
                        ParentCategoryName: rootCategory?.Name ?? "Uncategorized",
                        DaysInStage: stage.DaysSinceStartOrLastCompletion);
                })
                .ToList();

            if (selectedIds.Count > 0)
            {
                rows = rows
                    .Where(row =>
                        row.ParentCategoryId.HasValue
                        && selectedIds.Contains(row.ParentCategoryId.Value))
                    .ToList();
            }

            return rows;
            // END SECTION
        }

        private static IReadOnlyList<string> BuildOrderedStageCodes(IEnumerable<string> stageCodes)
        {
            // SECTION: Stage ordering helper
            var stageCodesPresent = stageCodes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return StageCodes.All
                .Where(code => stageCodesPresent.Contains(code, StringComparer.OrdinalIgnoreCase))
                .Concat(stageCodesPresent.Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // END SECTION
        }

        private async Task<IReadOnlyList<AnalyticsStageDurationPoint>> BuildOngoingStageDurationsAsync(
            CancellationToken cancellationToken)
        {
            var stageRows = await _db.ProjectStages
                .AsNoTracking()
                .Where(s => s.Project != null
                    && !s.Project.IsDeleted
                    && !s.Project.IsArchived
                    && s.Project.LifecycleStatus == ProjectLifecycleStatus.Active)
                .Select(s => new
                {
                    s.ProjectId,
                    s.StageCode,
                    s.SortOrder,
                    s.Status,
                    s.ActualStart,
                    s.CompletedOn
                })
                .ToListAsync(cancellationToken);

            // SECTION: Completion-driven stage duration aggregation. Completed-stage starts may be inferred.
            var durationSamples = new List<(string StageCode, double Duration)>();
            foreach (var projectStages in stageRows.GroupBy(row => row.ProjectId))
            {
                DateOnly? previousCompletion = null;
                foreach (var row in projectStages.OrderBy(item => item.SortOrder))
                {
                    if (string.IsNullOrWhiteSpace(row.StageCode))
                    {
                        continue;
                    }

                    var effectiveStart = row.ActualStart;
                    if (row.Status == StageStatus.Completed && !effectiveStart.HasValue && row.CompletedOn.HasValue && previousCompletion.HasValue)
                    {
                        var inferred = previousCompletion.Value.AddDays(1);
                        effectiveStart = inferred > row.CompletedOn.Value ? row.CompletedOn.Value : inferred;
                    }

                    if (effectiveStart.HasValue)
                    {
                        durationSamples.Add((row.StageCode, CalculateStageDurationDays(effectiveStart, row.CompletedOn)));
                    }

                    if (row.Status == StageStatus.Completed && row.CompletedOn.HasValue)
                    {
                        previousCompletion = row.CompletedOn;
                    }
                }
            }

            var durationListsByCode = durationSamples
                .GroupBy(sample => sample.StageCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(sample => sample.Duration).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var orderedStages = StageCodes.All
                .Select(code =>
                {
                    if (!durationListsByCode.TryGetValue(code, out var durations))
                    {
                        return new AnalyticsStageDurationPoint(
                            code,
                            StageCodes.DisplayNameOf(code),
                            0,
                            0,
                            0);
                    }

                    return new AnalyticsStageDurationPoint(
                        code,
                        StageCodes.DisplayNameOf(code),
                        durations.Count > 0 ? durations.Average() : 0,
                        CalculateMedian(durations),
                        durations.Count);
                });

            var adHocStages = durationListsByCode.Keys
                .Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase))
                .OrderBy(code => StageCodes.DisplayNameOf(code), StringComparer.OrdinalIgnoreCase)
                .Select(code =>
                {
                    var durations = durationListsByCode[code];
                    return new AnalyticsStageDurationPoint(
                        code,
                        StageCodes.DisplayNameOf(code),
                        durations.Count > 0 ? durations.Average() : 0,
                        CalculateMedian(durations),
                        durations.Count);
                });

            return orderedStages
                .Concat(adHocStages)
                .ToList();
            // END SECTION
        }

        // SECTION: Stage duration helpers
        private static double CalculateStageDurationDays(DateOnly? start, DateOnly? end)
        {
            if (!start.HasValue)
            {
                return 0;
            }

            var effectiveEnd = end ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return StageDurationCalculator.InclusiveCalendarDays(start.Value, effectiveEnd);
        }

        private static double CalculateMedian(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            var ordered = values.OrderBy(value => value).ToList();
            var mid = ordered.Count / 2;

            if (ordered.Count % 2 == 1)
            {
                return ordered[mid];
            }

            return (ordered[mid - 1] + ordered[mid]) / 2.0;
        }
        // END SECTION

        private PresentStageSnapshot ResolvePresentStage(ProjectStageSnapshot project)
        {
            var stages = project.Stages
                .Select(stage => new ProjectStageStatusSnapshot(
                    stage.StageCode,
                    stage.Status,
                    stage.SortOrder,
                    stage.ActualStart,
                    stage.CompletedOn))
                .ToList();

            return PresentStageHelper.ComputePresentStageAndAge(
                stages,
                _workflowStageMetadataProvider,
                project.WorkflowVersion,
                project.Status);
        }

        private static string ResolveName(int? id, IReadOnlyDictionary<int, string> lookup) =>
            id.HasValue && lookup.TryGetValue(id.Value, out var name)
                ? name
                : "Uncategorized";

        private sealed record StageSnapshot(
            string StageCode,
            StageStatus Status,
            int SortOrder,
            DateOnly? ActualStart,
            DateOnly? CompletedOn);

        private sealed record ProjectStageSnapshot(
            ProjectLifecycleStatus Status,
            string? WorkflowVersion,
            IReadOnlyList<StageSnapshot> Stages);

        private sealed record ProjectCategoryRow(
            int Id,
            string Name,
            int? ParentId,
            bool IsActive,
            int SortOrder);

        private sealed class CategoryAggregation
        {
            public int? Id { get; init; }
            public int Count { get; init; }
        }

        private sealed record OngoingStageRow(
            int ProjectId,
            string ProjectName,
            string StageCode,
            string StageName,
            int? ParentCategoryId,
            string ParentCategoryName,
            int? DaysInStage);
        // END SECTION

        public sealed record CategoryOption(int Id, string Name);
        public sealed record TechnicalCategoryOption(int Id, string Name);
        public sealed record AnalyticsFilterOption(int Id, string Name);
    }
}
