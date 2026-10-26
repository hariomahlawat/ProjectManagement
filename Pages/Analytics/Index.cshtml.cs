using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
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
        private readonly IPortfolioAnalyticsService _portfolioAnalytics;
        private readonly IArppAnalyticsService? _arppAnalytics;
        private readonly IArVrImpactAnalyticsService? _arVrImpact;
        private readonly IAuthorizationService? _authorization;

        /// <summary>True when the signed-in user may see the ARPP mode (the ARPP report's ViewArpp policy). Decides the switcher item; the server check is in OnGetAsync.</summary>
        public bool CanViewArpp { get; private set; }

        public IndexModel(
            ApplicationDbContext db,
            IProjectAnalyticsService projectAnalyticsService,
            IPortfolioAnalyticsService portfolioAnalytics,
            IArppAnalyticsService? arppAnalytics = null,
            IAuthorizationService? authorization = null,
            IArVrImpactAnalyticsService? arVrImpact = null)
        {
            _arVrImpact = arVrImpact;
            _db = db;
            _projectAnalyticsService = projectAnalyticsService;
            _portfolioAnalytics = portfolioAnalytics;
            _arppAnalytics = arppAnalytics;
            _authorization = authorization;
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

        /// <summary>True when a parent-category filter narrows the Ongoing mode; every Ongoing figure then follows it.</summary>
        public bool IsOngoingFiltered => ActiveOngoingStageParentCategoryIds.Count > 0;

        public int CompletedCount { get; private set; }
        public int OngoingCount { get; private set; }
        public int CoeCount { get; private set; }

        public CompletedAnalyticsVm? Completed { get; private set; }
        public OngoingAnalyticsVm? Ongoing { get; private set; }
        public CoeAnalyticsVm? Coe { get; private set; }

        /// <summary>CoE (AR/VR) mode: proliferation and training of every AR/VR project; null when the service is not wired (unit tests).</summary>
        public ArVrImpactVm? CoeArVrImpact { get; private set; }
        public StageTimeInsightsPanelVm? Insights { get; private set; }

        // SECTION: Page header and headline figures
        private static readonly TimeZoneInfo IndiaStandardTime = IstClock.TimeZone;

        /// <summary>When the figures were read, in IST; shown as the page's "as of" date.</summary>
        public DateTimeOffset AsOfIst { get; private set; }

        /// <summary>The IST calendar day every "days in stage" figure is measured against: the "as of" date, or IST now when a caller skipped OnGet.</summary>
        private DateOnly ReferenceDateIst =>
            DateOnly.FromDateTime((AsOfIst == default ? TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IndiaStandardTime) : AsOfIst).DateTime);

        /// <summary>Three or four large figures that open the active mode, computed from data already loaded.</summary>
        public IReadOnlyList<HeadlineFigure> Headlines { get; private set; } = Array.Empty<HeadlineFigure>();

        /// <summary>Workflow stages in order, so every stage chart draws the same axis.</summary>
        public IReadOnlyList<WorkflowStageOption> WorkflowStages { get; } = StageCodes.All
            .Select(code => new WorkflowStageOption(code, StageCodes.DisplayNameOf(code)))
            .ToList();

        /// <param name="DrillStageCodes">When set, the figure is a button that opens those stages' rows in the board on the page.</param>
        public sealed record HeadlineFigure(string Label, string Value, string? Hint = null, string? Href = null, string? DrillStageCodes = null);
        public sealed record WorkflowStageOption(string Code, string Name);
        // END SECTION

        public ProjectLifecycleFilter DefaultLifecycle => ProjectLifecycleFilter.Active;

        public int LifecycleViewCount => LifecycleFilters.Length;

        public int CategoryCount => Categories.Count;

        public int TechnicalGroupCount => TechnicalCategories.Count;

        public async Task<IActionResult> OnGetAsync(string? tab, CancellationToken cancellationToken)
        {
            ActiveTab = tab?.ToLowerInvariant() switch
            {
                "completed" => AnalyticsTab.Completed,
                "coe" => AnalyticsTab.Coe,
                "insights" => AnalyticsTab.Insights,
                "arpp" => AnalyticsTab.Arpp,
                _ => AnalyticsTab.Ongoing
            };

            // SECTION: ARPP mode authorization (same policy as the ARPP library and the FY report)
            CanViewArpp = await CanViewArppAsync();
            if (ActiveTab == AnalyticsTab.Arpp && !CanViewArpp)
            {
                return Forbid();
            }
            // END SECTION

            AsOfIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IndiaStandardTime);
            await LoadAnalyticsAsync(cancellationToken);

            // SECTION: Active tab hydration
            switch (ActiveTab)
            {
                case AnalyticsTab.Completed:
                    Completed = await _portfolioAnalytics.BuildCompletedAsync(ReferenceDateIst, cancellationToken);
                    CompletedCount = Completed.TotalCompletedProjects;
                    break;

                case AnalyticsTab.Ongoing:
                    // OngoingCount (the switcher badge) stays the portfolio total; the figures follow the filter.
                    Ongoing = await _portfolioAnalytics.BuildOngoingAsync(
                        ActiveOngoingStageParentCategoryIds,
                        OngoingStageParentCategoryOptions.Select(option => option.Id).ToList(),
                        ReferenceDateIst,
                        cancellationToken);
                    break;

                case AnalyticsTab.Coe:
                    Coe = await _portfolioAnalytics.BuildCoeAsync(ReferenceDateIst, cancellationToken);
                    CoeCount = Coe.TotalCoeProjects;
                    // The two trackers keep their own view policies on this page: a card is built and drawn only for a caller its tracker admits.
                    if (_arVrImpact is not null)
                    {
                        var canViewProliferation = await PassesPolicyAsync(ProjectOfficeReportsPolicies.ViewProliferationTracker);
                        var canViewTraining = await PassesPolicyAsync(ProjectOfficeReportsPolicies.ViewTrainingTracker);
                        CoeArVrImpact = canViewProliferation || canViewTraining
                            ? await _arVrImpact.BuildAsync(ReferenceDateIst, canViewProliferation, canViewTraining, cancellationToken)
                            : null;
                    }
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
                            CategoryRows = stageCycleResult.CategoryRows,
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
            return Page();
        }

        private Task<bool> CanViewArppAsync() => PassesPolicyAsync(ProjectOfficeReportsPolicies.ViewArpp);

        /// <summary>Fail closed: without an authorization service or a signed-in principal, no policy passes.</summary>
        private async Task<bool> PassesPolicyAsync(string policy)
        {
            if (_authorization is null || User?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            var result = await _authorization.AuthorizeAsync(User, policy);
            return result.Succeeded;
        }

        // SECTION: Headline figures
        private IReadOnlyList<HeadlineFigure> BuildHeadlines()
        {
            var completedUrl = PageUrl("/Projects/Index", new { Lifecycle = ProjectLifecycleFilter.Completed });

            switch (ActiveTab)
            {
                case AnalyticsTab.Ongoing when Ongoing is not null:
                    // One card per workflow phase; the total is the switcher badge and the header line.
                    // A card opens that phase's stage rows in the board (data-phase-drill).
                    return Ongoing.Phases
                        .Select(phase => new HeadlineFigure(
                            phase.Name,
                            phase.Count.ToString(),
                            $"{phase.Span} · {(phase.PastThresholdCount == 0 ? "none" : phase.PastThresholdCount.ToString())} past {OngoingAnalyticsVm.LongStageThresholdDays} days",
                            DrillStageCodes: string.Join(",", phase.StageCodes)))
                        .ToList();

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
                            "Average per year",
                            Completed.RecentYearlyAverage is null ? "—" : Completed.RecentYearlyAverage.PerYear.ToString("0.0"),
                            YearlyAverageHint(Completed.RecentYearlyAverage, Completed.PreviousYearlyAverage)),
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
                {
                    // The three stages where the most time goes, then the baseline they are read against.
                    var labels = new[] { "Slowest stage", "Second slowest", "Third slowest" };
                    var figures = labels
                        .Select((label, index) =>
                        {
                            var stage = index < Insights.RankedHotspots.Count ? Insights.RankedHotspots[index] : null;
                            return new HeadlineFigure(
                                label,
                                stage?.StageName ?? "—",
                                stage is null ? null : HotspotHint(stage));
                        })
                        .ToList();
                    var measured = Insights.RankedHotspots.Count;
                    figures.Add(new HeadlineFigure(
                        "Portfolio median",
                        FormatDays(measured > 0 ? Insights.PortfolioMedianDays : null),
                        measured > 0 ? $"days per stage, across {measured} {Plural(measured, "stage")} measured" : "no finished stages yet"));
                    return figures;
                }

                case AnalyticsTab.Arpp when Arpp is not null && Arpp.HasData:
                    return new[]
                    {
                        // Listed, then where they stand: completed, supply order placed, and the rest still before
                        // a supply order. Cancelled projects are in no card; the standing board lists them.
                        new HeadlineFigure(
                            "Listed projects",
                            Arpp.ListedCount.ToString(),
                            $"IPA {IndianCurrencyFormatter.FormatCompact(Arpp.ListedIpaValue)} · New {Arpp.NewCount} · CL {Arpp.CommittedLiabilityCount} · CF {Arpp.CarryForwardCount} · delisted {Arpp.DelistedCount}"),
                        new HeadlineFigure(
                            "Completed",
                            Arpp.CompletedCount.ToString(),
                            Arpp.CompletedSupplyOrderValue > 0
                                ? $"supply orders {IndianCurrencyFormatter.FormatCompact(Arpp.CompletedSupplyOrderValue)}"
                                : "no supply-order value recorded"),
                        new HeadlineFigure(
                            "Supply orders placed",
                            Arpp.SupplyOrderCount.ToString(),
                            Arpp.SupplyOrderValue > 0
                                ? $"supply orders {IndianCurrencyFormatter.FormatCompact(Arpp.SupplyOrderValue)} · ongoing"
                                : "ongoing; no value recorded yet"),
                        new HeadlineFigure(
                            "AoN to SO stage",
                            Arpp.BeforeSupplyOrderCount.ToString(),
                            Arpp.BeforeAonCount > 0
                                ? $"ongoing, supply order not yet placed · {Arpp.BeforeAonCount} still before AoN"
                                : "ongoing, supply order not yet placed")
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

            portfolioMedian = AnalyticsAggregation.CalculateMedian(measured.Select(point => point.MedianDays));
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

        /// <summary>"75 days median · 3.6× portfolio median · 12 projects", with "small sample" under three projects.</summary>
        private static string HotspotHint(StageHotspotRankVm stage)
        {
            var days = (int)Math.Round(stage.MedianDays);
            var ratio = stage.RatioToPortfolioMedian >= 10 ? $"{stage.RatioToPortfolioMedian:0}×" : $"{stage.RatioToPortfolioMedian:0.0}×";
            var sample = stage.ProjectCount < 3 ? ", small sample" : "";
            return $"{days} {Plural(days, "day")} median · {ratio} portfolio median · {stage.ProjectCount} {Plural(stage.ProjectCount, "project")}{sample}";
        }

        /// <summary>"2021 to 2025 · 61 completed · 8.4 a year in the five before"; the comparison names its span when it is shorter.</summary>
        private static string YearlyAverageHint(CompletedYearlyAverageVm? recent, CompletedYearlyAverageVm? previous)
        {
            if (recent is null)
            {
                return "no complete year yet";
            }

            var span = recent.Years == 1 ? recent.ToYear.ToString() : $"{recent.FromYear} to {recent.ToYear}";
            var hint = $"{span} · {recent.Count} completed";
            if (previous is not null)
            {
                var before = previous.Years == 5 ? "in the five before" : previous.Years == 1 ? $"in {previous.ToYear}" : $"in the {previous.Years} years before";
                hint += $" · {previous.PerYear:0.0} a year {before}";
            }

            return hint;
        }

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

        // SECTION: Filter options and switcher badges (the mode figures come from the services)
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

            var requestedParentCategoryIds = OngoingStageParentCategoryIds
                .Where(id => id > 0)
                .Distinct()
                .Where(id => OngoingStageParentCategoryOptions.Any(option => option.Id == id))
                .ToList();
            // "Select all" is no filter: uncategorized projects and inactive roots stay in the figures.
            ActiveOngoingStageParentCategoryIds = requestedParentCategoryIds.Count >= OngoingStageParentCategoryOptions.Count
                ? Array.Empty<int>()
                : requestedParentCategoryIds;

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

            // The CoE badge is a count; the CoE tab sets it from the full aggregation instead.
            if (ActiveTab != AnalyticsTab.Coe)
            {
                CoeCount = await _portfolioAnalytics.CountCoeProjectsAsync(cancellationToken);
            }
        }

        private sealed record ProjectCategoryRow(
            int Id,
            string Name,
            int? ParentId,
            bool IsActive,
            int SortOrder);
        // END SECTION

        public sealed record CategoryOption(int Id, string Name);
        public sealed record TechnicalCategoryOption(int Id, string Name);
        public sealed record AnalyticsFilterOption(int Id, string Name);
    }
}
