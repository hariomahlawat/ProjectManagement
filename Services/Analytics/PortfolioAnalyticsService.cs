using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Analytics
{
    /// <summary>
    /// Analytics → Ongoing, Completed and CoE: the portfolio aggregations behind those three modes.
    /// The page passes the IST "as of" day in; every "days in stage" and "this year" figure is measured
    /// against it. The view models keep the shapes the partials and the chart script expect.
    /// </summary>
    public interface IPortfolioAnalyticsService
    {
        /// <summary>
        /// The Ongoing mode from one load of the active projects with their stages. The figures follow
        /// the parent-category filter when one is given; an empty filter is the whole portfolio.
        /// </summary>
        /// <param name="selectedParentCategoryIds">Root category ids that narrow every figure, or empty for no filter.</param>
        /// <param name="orderedParentCategoryIds">Root category ids in the order the stage board lists them.</param>
        /// <param name="referenceDateIst">The IST day that "days in stage" is measured against.</param>
        Task<OngoingAnalyticsVm> BuildOngoingAsync(
            IReadOnlyCollection<int> selectedParentCategoryIds,
            IReadOnlyList<int> orderedParentCategoryIds,
            DateOnly referenceDateIst,
            CancellationToken cancellationToken = default);

        /// <param name="referenceDateIst">The IST day whose year is "this year".</param>
        Task<CompletedAnalyticsVm> BuildCompletedAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default);

        /// <param name="referenceDateIst">The IST day that "days in stage" and "this year" are measured against.</param>
        Task<CoeAnalyticsVm> BuildCoeAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default);

        /// <summary>The CoE project total alone (the switcher badge), without the full CoE aggregation.</summary>
        Task<int> CountCoeProjectsAsync(CancellationToken cancellationToken = default);
    }

    public sealed class PortfolioAnalyticsService : IPortfolioAnalyticsService
    {
        // SECTION: Analytics constants
        private const string DefaultCoeSubcategoryName = "Unspecified";
        private const int MaxCoeSubcategoryBuckets = 10;
        private const string UnassignedStageCode = "UNASSIGNED";
        private const string UnassignedStageName = "Unassigned";
        // END SECTION
        // AR/VR is a technical category; the CoE is a project category. The two are matched by name, as CoE categories are.
        private static readonly string[] ArVrTechnicalCategoryKeywords = { "ar/vr", "arvr", "ar&vr", "augmented", "virtualreality" };
        private const string DcdCategoryKeyword = "dcd";

        private static readonly string[] CoeCategoryKeywords =
        {
            "coe",
            "center of excellence",
            "centre of excellence",
            "centers of excellence",
            "centres of excellence"
        };

        private readonly ApplicationDbContext _db;
        private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;

        public PortfolioAnalyticsService(
            ApplicationDbContext db,
            IWorkflowStageMetadataProvider workflowStageMetadataProvider)
        {
            _db = db;
            _workflowStageMetadataProvider = workflowStageMetadataProvider;
        }

        /// <summary>The drill-down key the charts use for a category: its id, or "none" for projects without one.</summary>
        internal static string CategoryDrillKey(int? id) => id?.ToString() ?? "none";

        // SECTION: Ongoing analytics aggregation (one projection, three outputs)
        public async Task<OngoingAnalyticsVm> BuildOngoingAsync(
            IReadOnlyCollection<int> selectedParentCategoryIds,
            IReadOnlyList<int> orderedParentCategoryIds,
            DateOnly referenceDateIst,
            CancellationToken cancellationToken = default)
        {
            var hierarchy = await LoadCategoryHierarchyAsync(cancellationToken);
            var stageRows = await BuildOngoingStageRowsAsync(selectedParentCategoryIds, hierarchy, referenceDateIst, cancellationToken);

            var byStageByParentCategory = BuildOngoingStageDistributionByParentCategory(stageRows);
            var stageBoard = BuildOngoingStageBoard(stageRows, orderedParentCategoryIds);

            // SECTION: Headline figures from the (filtered) stage rows, the total included
            var total = stageRows.Count;
            var topStage = stageBoard.OrderByDescending(item => item.StageCount).FirstOrDefault();
            var stageAges = stageRows
                .Where(row => row.DaysInStage.HasValue)
                .Select(row => (double)row.DaysInStage!.Value)
                .ToList();
            // END SECTION

            return new OngoingAnalyticsVm
            {
                TotalOngoingProjects = total,
                ByStageByParentCategory = byStageByParentCategory,
                StageBoard = stageBoard,
                TopStageName = topStage?.StageName,
                TopStageCount = topStage?.StageCount ?? 0,
                MedianDaysInCurrentStage = stageAges.Count == 0 ? null : AnalyticsAggregation.CalculateMedian(stageAges),
                ProjectsPastThreshold = stageAges.Count(days => days > OngoingAnalyticsVm.LongStageThresholdDays),
                Phases = BuildOngoingPhases(stageRows)
            };
        }

        // SECTION: Phase cards (the phase of each project follows its current stage; no stage at all is Pre-AoN)
        private static IReadOnlyList<OngoingPhaseVm> BuildOngoingPhases(IReadOnlyList<OngoingStageRow> stageRows)
        {
            var phaseOfRow = stageRows
                .Select(row => (Row: row, Phase: StagePhases.Of(string.Equals(row.StageCode, UnassignedStageCode, StringComparison.OrdinalIgnoreCase) ? null : row.StageCode)))
                .Where(item => item.Phase.HasValue)
                .ToList();

            return StagePhases.All
                .Select(definition =>
                {
                    var rows = phaseOfRow.Where(item => item.Phase == definition.Phase).Select(item => item.Row).ToList();
                    return new OngoingPhaseVm(
                        definition.Key,
                        definition.Name,
                        definition.Span,
                        definition.StageCodes,
                        rows.Count,
                        rows.Count(row => row.DaysInStage > OngoingAnalyticsVm.LongStageThresholdDays));
                })
                .ToList();
        }
        // END SECTION

        private async Task<IReadOnlyList<OngoingStageRow>> BuildOngoingStageRowsAsync(
            IReadOnlyCollection<int>? selectedParentCategoryIds,
            IReadOnlyDictionary<int, ProjectCategoryHierarchyNode> hierarchy,
            DateOnly referenceDateIst,
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
                    var rootCategory = ProjectCategoryHierarchyResolver.ResolveRoot(snapshot.CategoryId, hierarchy);
                    var stage = ResolvePresentStage(
                        new ProjectStageSnapshot(snapshot.LifecycleStatus, snapshot.WorkflowVersion, snapshot.Stages),
                        referenceDateIst);
                    var stageCode = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                        ? UnassignedStageCode
                        : stage.CurrentStageCode.Trim();
                    var stageName = string.Equals(stageCode, UnassignedStageCode, StringComparison.OrdinalIgnoreCase)
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
                    .Where(row => row.ParentCategoryId.HasValue && selectedIds.Contains(row.ParentCategoryId.Value))
                    .ToList();
            }

            return rows;
            // END SECTION
        }

        private static IReadOnlyList<OngoingStageByParentCategoryPoint> BuildOngoingStageDistributionByParentCategory(
            IReadOnlyList<OngoingStageRow> stageRows)
        {
            // SECTION: Ongoing stage distribution by parent category
            if (stageRows.Count == 0)
            {
                return Array.Empty<OngoingStageByParentCategoryPoint>();
            }

            var stageOrderLookup = AnalyticsAggregation.BuildStageOrderLookup(
                AnalyticsAggregation.BuildOrderedStageCodes(stageRows.Select(row => row.StageCode)));

            return stageRows
                .GroupBy(row => new { row.StageCode, row.StageName, row.ParentCategoryName })
                .Select(group => new OngoingStageByParentCategoryPoint(
                    StageCode: group.Key.StageCode,
                    StageName: group.Key.StageName,
                    CategoryName: group.Key.ParentCategoryName,
                    Count: group.Count()))
                .OrderBy(point => AnalyticsAggregation.ResolveStageOrder(point.StageCode, stageOrderLookup))
                .ThenBy(point => point.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // END SECTION
        }

        private static IReadOnlyList<OngoingStageBoardItemVm> BuildOngoingStageBoard(
            IReadOnlyList<OngoingStageRow> stageRows,
            IReadOnlyList<int> orderedParentCategoryIds)
        {
            // SECTION: Ongoing stage drill-down board
            if (stageRows.Count == 0)
            {
                return Array.Empty<OngoingStageBoardItemVm>();
            }

            var stageOrderLookup = AnalyticsAggregation.BuildStageOrderLookup(
                AnalyticsAggregation.BuildOrderedStageCodes(stageRows.Select(row => row.StageCode)));
            var parentCategoryOrder = orderedParentCategoryIds
                .Select((id, index) => new { Id = id, Order = index })
                .GroupBy(x => x.Id)
                .ToDictionary(group => group.Key, group => group.First().Order);

            return stageRows
                .GroupBy(row => new { row.StageCode, row.StageName })
                .OrderBy(group => AnalyticsAggregation.ResolveStageOrder(group.Key.StageCode, stageOrderLookup))
                .Select(stageGroup =>
                {
                    var categories = stageGroup
                        .GroupBy(row => new { row.ParentCategoryId, row.ParentCategoryName })
                        .OrderBy(group =>
                            group.Key.ParentCategoryId.HasValue
                            && parentCategoryOrder.TryGetValue(group.Key.ParentCategoryId.Value, out var categoryOrder)
                                ? categoryOrder
                                : int.MaxValue)
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
        // END SECTION

        // SECTION: Completed analytics aggregation
        public async Task<CompletedAnalyticsVm> BuildCompletedAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default)
        {
            var hierarchy = await LoadCategoryHierarchyAsync(cancellationToken);
            var completedQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

            var byCategory = await BuildCategoryCountsAsync(completedQuery, cancellationToken);
            var byTechnical = await BuildTechnicalCategoryCountsAsync(completedQuery, cancellationToken);
            var perYearByParentCategory = await BuildCompletedPerYearByParentCategoryAsync(completedQuery, hierarchy, cancellationToken);
            var yearBoard = await BuildCompletedYearBoardAsync(completedQuery, hierarchy, cancellationToken);

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

            // SECTION: Headline figures (this year, yearly average, median time to complete)
            var currentYear = referenceDateIst.Year;
            var (recentAverage, previousAverage) = BuildYearlyAverages(perYear, currentYear);
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
                RecentYearlyAverage = recentAverage,
                PreviousYearlyAverage = previousAverage,
                MedianDaysToComplete = medianDaysToComplete,
                CategoryBoard = categoryBoard,
                TechnicalBoard = technicalBoard
            };
        }

        // SECTION: Yearly averages over complete calendar years
        private const int YearlyAverageWindow = 5;

        /// <summary>
        /// The last five complete calendar years (the current, partial year is left out) and the five
        /// before them. A window is clipped to the first year anything was completed, so a short history
        /// averages over the years it has; a year inside a window with no completions counts as zero.
        /// </summary>
        internal static (CompletedYearlyAverageVm? Recent, CompletedYearlyAverageVm? Previous) BuildYearlyAverages(
            IReadOnlyList<CompletedPerYearPoint> perYear,
            int currentYear)
        {
            if (perYear.Count == 0)
            {
                return (null, null);
            }

            var firstYear = perYear.Min(point => point.Year);
            var recent = Window(perYear, firstYear, toYear: currentYear - 1);
            var previous = recent is null ? null : Window(perYear, firstYear, toYear: recent.FromYear - 1);
            return (recent, previous);
        }

        private static CompletedYearlyAverageVm? Window(IReadOnlyList<CompletedPerYearPoint> perYear, int firstYear, int toYear)
        {
            var fromYear = Math.Max(toYear - (YearlyAverageWindow - 1), firstYear);
            if (fromYear > toYear)
            {
                return null;
            }

            var count = perYear.Where(point => point.Year >= fromYear && point.Year <= toYear).Sum(point => point.Count);
            var years = toYear - fromYear + 1;
            return new CompletedYearlyAverageVm(fromYear, toYear, count, (double)count / years);
        }
        // END SECTION

        // SECTION: Completed drill-downs by exact category and technical category (same grouping as the charts)
        private static async Task<(IReadOnlyList<AnalyticsBoardGroupVm> CategoryBoard, IReadOnlyList<AnalyticsBoardGroupVm> TechnicalBoard)>
            BuildCompletedGroupBoardsAsync(IQueryable<Project> completedQuery, CancellationToken cancellationToken)
        {
            var rows = await completedQuery
                .Select(project => new CompletedBoardRow(
                    project.Id,
                    project.Name,
                    project.CategoryId,
                    project.Category != null ? project.Category.Name : null,
                    project.TechnicalCategoryId,
                    project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                    project.CompletedYear ?? (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : (int?)null)))
                .ToListAsync(cancellationToken);

            // Grouped and keyed by category id, as the charts count: names are unique only per parent.
            IReadOnlyList<AnalyticsBoardGroupVm> Group(Func<CompletedBoardRow, int?> idOf, Func<CompletedBoardRow, string?> nameOf) => rows
                .GroupBy(row => idOf(row))
                .Select(group => new AnalyticsBoardGroupVm(
                    CategoryDrillKey(group.Key),
                    group.Select(nameOf).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.Trim() ?? "Uncategorized",
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

            return (Group(row => row.CategoryId, row => row.CategoryName), Group(row => row.TechnicalCategoryId, row => row.TechnicalName));
        }
        // END SECTION

        // SECTION: Median time to complete (first stage start to completion)
        private static async Task<double?> BuildMedianDaysToCompleteAsync(
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

            return spans.Count == 0 ? null : AnalyticsAggregation.CalculateMedian(spans);
        }
        // END SECTION

        private async Task<IReadOnlyList<AnalyticsCategoryCountPoint>> BuildCategoryCountsAsync(
            IQueryable<Project> projectQuery,
            CancellationToken cancellationToken)
        {
            var categoryCounts = await projectQuery
                .GroupBy(p => p.CategoryId)
                .Select(g => new CategoryAggregation { Id = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(cancellationToken);

            var namedCategories = await LoadCategoryNamesAsync(categoryCounts, cancellationToken);

            return categoryCounts
                .Select(item => new AnalyticsCategoryCountPoint(ResolveName(item.Id, namedCategories), item.Count, item.Id))
                .ToList();
        }

        private async Task<IReadOnlyList<AnalyticsCategoryCountPoint>> BuildTechnicalCategoryCountsAsync(
            IQueryable<Project> projectQuery,
            CancellationToken cancellationToken)
        {
            var technicalCounts = await projectQuery
                .GroupBy(p => p.TechnicalCategoryId)
                .Select(g => new CategoryAggregation { Id = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(cancellationToken);

            var namedTechnicalCategories = await LoadTechnicalCategoryNamesAsync(technicalCounts, cancellationToken);

            return technicalCounts
                .Select(item => new AnalyticsCategoryCountPoint(ResolveName(item.Id, namedTechnicalCategories), item.Count, item.Id))
                .ToList();
        }

        private static async Task<IReadOnlyList<CompletedPerYearByParentCategoryPoint>> BuildCompletedPerYearByParentCategoryAsync(
            IQueryable<Project> completedQuery,
            IReadOnlyDictionary<int, ProjectCategoryHierarchyNode> hierarchy,
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
                    RootCategory = ProjectCategoryHierarchyResolver.ResolveRoot(row.CategoryId, hierarchy)
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
        private static async Task<IReadOnlyList<CompletedYearBoardItemVm>> BuildCompletedYearBoardAsync(
            IQueryable<Project> completedQuery,
            IReadOnlyDictionary<int, ProjectCategoryHierarchyNode> hierarchy,
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
                    ProjectName = string.IsNullOrWhiteSpace(row.Name) ? "Untitled project" : row.Name.Trim(),
                    row.CompletedOn,
                    EffectiveYear = row.CompletedYear ?? row.CompletedOn?.Year,
                    ParentCategoryName = ProjectCategoryHierarchyResolver.ResolveRoot(row.CategoryId, hierarchy)?.Name ?? "Unassigned"
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
            var ids = aggregations.Where(a => a.Id.HasValue).Select(a => a.Id!.Value).Distinct().ToList();
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
            var ids = aggregations.Where(a => a.Id.HasValue).Select(a => a.Id!.Value).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            return await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        }
        // END SECTION

        // SECTION: CoE analytics aggregation
        public async Task<int> CountCoeProjectsAsync(CancellationToken cancellationToken = default)
        {
            var coeCategoryIds = await LoadCoeCategoryIdsAsync(cancellationToken);
            if (coeCategoryIds.Count == 0)
            {
                return 0;
            }

            return await CoeProjectsQuery(coeCategoryIds).CountAsync(cancellationToken);
        }

        public async Task<CoeAnalyticsVm> BuildCoeAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default)
        {
            var coeCategoryIds = await LoadCoeCategoryIdsAsync(cancellationToken);
            var arVrOutsideCoe = await BuildArVrOutsideCoeAsync(coeCategoryIds, referenceDateIst, cancellationToken);
            if (coeCategoryIds.Count == 0)
            {
                return BuildEmptyCoeAnalyticsVm(arVrOutsideCoe);
            }

            var coeProjectsQuery = CoeProjectsQuery(coeCategoryIds);

            var totalCoeProjects = await coeProjectsQuery.CountAsync(cancellationToken);
            if (totalCoeProjects == 0)
            {
                return BuildEmptyCoeAnalyticsVm(arVrOutsideCoe);
            }

            var stageBuckets = await BuildCoeStageBucketsAsync(coeCategoryIds, referenceDateIst, cancellationToken);
            var subcategoryBreakdown = await BuildCoeSubcategoryBreakdownAsync(coeProjectsQuery, cancellationToken);
            var subcategoryProjects = await BuildCoeSubcategoryProjectsAsync(coeProjectsQuery, referenceDateIst, cancellationToken);

            // SECTION: Headline figures
            var currentYear = referenceDateIst.Year;
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
                OverflowProjects = BuildCoeOverflowProjects(subcategoryBreakdown, subcategoryProjects),
                TotalCoeProjects = totalCoeProjects,
                OngoingCount = ongoingCount,
                CompletedCount = completedCount,
                CompletedThisYearCount = completedThisYear,
                ArVrOutsideCoe = arVrOutsideCoe
            };
        }

        // SECTION: AR/VR projects started before the CoE nomination (outside the CoE categories; DCD fresh projects and repeat builds)
        private async Task<IReadOnlyList<AnalyticsBoardGroupVm>> BuildArVrOutsideCoeAsync(
            List<int> coeCategoryIds,
            DateOnly referenceDateIst,
            CancellationToken cancellationToken)
        {
            var arVrTechnicalIds = await _db.TechnicalCategories
                .AsNoTracking()
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(cancellationToken);
            var technicalIds = arVrTechnicalIds.Where(t => IsArVrTechnicalCategory(t.Name)).Select(t => t.Id).ToList();

            var rows = technicalIds.Count == 0
                ? new List<ArVrProjectRow>()
                : await _db.Projects
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted
                        && !p.IsArchived
                        && p.TechnicalCategoryId.HasValue
                        && technicalIds.Contains(p.TechnicalCategoryId.Value)
                        && (!p.CategoryId.HasValue || !coeCategoryIds.Contains(p.CategoryId.Value)))
                    .Select(p => new ArVrProjectRow(
                        p.Id,
                        p.Name,
                        p.LifecycleStatus,
                        p.CompletedYear ?? (p.CompletedOn.HasValue ? p.CompletedOn.Value.Year : (int?)null),
                        p.IsBuild,
                        p.CategoryId,
                        p.WorkflowVersion,
                        p.ProjectStages
                            .OrderBy(s => s.SortOrder)
                            .ThenBy(s => s.StageCode)
                            .Select(s => new StageSnapshot(s.StageCode, s.Status, s.SortOrder, s.ActualStart, s.CompletedOn))
                            .ToList()))
                    .ToListAsync(cancellationToken);

            var hierarchy = rows.Count == 0
                ? new Dictionary<int, ProjectCategoryHierarchyNode>()
                : await LoadCategoryHierarchyAsync(cancellationToken);

            var placed = rows
                .Select(row =>
                {
                    var root = ProjectCategoryHierarchyResolver.ResolveRoot(row.CategoryId, hierarchy)?.Name;
                    var underDcd = root is not null && root.Contains(DcdCategoryKeyword, StringComparison.OrdinalIgnoreCase);
                    var key = !underDcd ? CoeAnalyticsVm.ArVrEarlierKey : row.IsBuild ? CoeAnalyticsVm.ArVrDcdRepeatKey : CoeAnalyticsVm.ArVrDcdFreshKey;
                    return (Key: key, Row: row);
                })
                .ToList();

            AnalyticsBoardGroupVm Group(string key, string name)
            {
                var members = placed.Where(item => item.Key == key).Select(item => item.Row).ToList();
                // Completed projects first, oldest completion first; then ongoing, then cancelled, each by name.
                var projects = members
                    .OrderBy(row => LifecycleRank(row.LifecycleStatus))
                    .ThenBy(row => row.LifecycleStatus == ProjectLifecycleStatus.Completed ? row.CompletedYear ?? int.MaxValue : 0)
                    .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new AnalyticsBoardProjectVm(
                        row.Id,
                        string.IsNullOrWhiteSpace(row.Name) ? "Untitled project" : row.Name.Trim(),
                        DescribeArVrProject(row, referenceDateIst)))
                    .ToList();
                var completed = members.Count(row => row.LifecycleStatus == ProjectLifecycleStatus.Completed);
                var ongoing = members.Count(row => row.LifecycleStatus == ProjectLifecycleStatus.Active);
                var cancelled = members.Count(row => row.LifecycleStatus == ProjectLifecycleStatus.Cancelled);
                var hint = members.Count == 0
                    ? null
                    : string.Join(" · ", new[]
                    {
                        completed > 0 ? $"{completed} completed" : null,
                        ongoing > 0 ? $"{ongoing} ongoing" : null,
                        cancelled > 0 ? $"{cancelled} cancelled" : null
                    }.Where(part => part is not null));
                return new AnalyticsBoardGroupVm(key, name, projects.Count, projects, hint);
            }

            return new[]
            {
                Group(CoeAnalyticsVm.ArVrEarlierKey, "Before the CoE nomination"),
                Group(CoeAnalyticsVm.ArVrDcdFreshKey, "DCD · Fresh projects"),
                Group(CoeAnalyticsVm.ArVrDcdRepeatKey, "DCD · Repeat builds")
            };
        }

        private string DescribeArVrProject(ArVrProjectRow row, DateOnly referenceDateIst)
        {
            switch (row.LifecycleStatus)
            {
                case ProjectLifecycleStatus.Completed:
                    return row.CompletedYear.HasValue ? $"Completed {row.CompletedYear}" : "Completed";
                case ProjectLifecycleStatus.Cancelled:
                    return "Cancelled";
                default:
                    var stage = ResolvePresentStage(new ProjectStageSnapshot(row.LifecycleStatus, row.WorkflowVersion, row.Stages), referenceDateIst);
                    var stageName = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                        ? null
                        : stage.CurrentStageName ?? StageCodes.DisplayNameOf(stage.CurrentStageCode);
                    return stageName is null ? "Ongoing" : $"Ongoing · {stageName}";
            }
        }

        private static int LifecycleRank(ProjectLifecycleStatus status) => status switch
        {
            ProjectLifecycleStatus.Completed => 0,
            ProjectLifecycleStatus.Active => 1,
            _ => 2
        };

        private static bool IsArVrTechnicalCategory(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var compact = name.Replace(" ", string.Empty).ToLowerInvariant();
            return ArVrTechnicalCategoryKeywords.Any(keyword => compact.Contains(keyword, StringComparison.Ordinal));
        }

        private sealed record ArVrProjectRow(
            int Id,
            string? Name,
            ProjectLifecycleStatus LifecycleStatus,
            int? CompletedYear,
            bool IsBuild,
            int? CategoryId,
            string? WorkflowVersion,
            IReadOnlyList<StageSnapshot> Stages);
        // END SECTION

        private IQueryable<Project> CoeProjectsQuery(List<int> coeCategoryIds) =>
            _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted
                    && !p.IsArchived
                    && p.CategoryId.HasValue
                    && coeCategoryIds.Contains(p.CategoryId.Value));

        private async Task<IReadOnlyList<CoeStageBucketVm>> BuildCoeStageBucketsAsync(
            List<int> coeCategoryIds,
            DateOnly referenceDateIst,
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
                var stage = ResolvePresentStage(project, referenceDateIst);
                var stageCode = string.IsNullOrWhiteSpace(stage.CurrentStageCode)
                    ? UnassignedStageCode
                    : stage.CurrentStageCode.Trim();

                counts.TryGetValue(stageCode, out var existing);
                counts[stageCode] = existing + 1;
            }

            // Every workflow stage draws, even at zero; ad hoc stages follow.
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
                        StageName: string.Equals(code, UnassignedStageCode, StringComparison.OrdinalIgnoreCase) ? UnassignedStageName : StageCodes.DisplayNameOf(code),
                        ProjectCount: count);
                })
                .ToList();
            // END SECTION
        }

        private static async Task<IReadOnlyList<CoeSubcategoryLifecycleVm>> BuildCoeSubcategoryBreakdownAsync(
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
                    overflow.Sum(bucket => bucket.Total),
                    CoeAnalyticsVm.OverflowSubcategoryKey));
            }

            return primaryBuckets;
            // END SECTION
        }

        private async Task<IReadOnlyList<CoeSubcategoryProjectsVm>> BuildCoeSubcategoryProjectsAsync(
            IQueryable<Project> coeProjectsQuery,
            DateOnly referenceDateIst,
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

            return projectRows
                .Select(row =>
                {
                    var stage = ResolvePresentStage(
                        new ProjectStageSnapshot(row.LifecycleStatus, row.WorkflowVersion, row.Stages),
                        referenceDateIst);
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
            // END SECTION
        }

        // SECTION: CoE overflow drill-down (the sub-categories folded into the chart's "Other" bucket, as one row)
        private static CoeSubcategoryProjectsVm? BuildCoeOverflowProjects(
            IReadOnlyList<CoeSubcategoryLifecycleVm> buckets,
            IReadOnlyList<CoeSubcategoryProjectsVm> projectsBySubcategory)
        {
            if (!buckets.Any(bucket => bucket.Key == CoeAnalyticsVm.OverflowSubcategoryKey))
            {
                return null;
            }

            var charted = buckets
                .Where(bucket => bucket.Key != CoeAnalyticsVm.OverflowSubcategoryKey)
                .Select(bucket => bucket.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var folded = projectsBySubcategory
                .Where(bucket => !charted.Contains(bucket.SubcategoryName))
                .ToList();

            return new CoeSubcategoryProjectsVm(
                $"Other ({folded.Count} sub-categories)",
                folded
                    .SelectMany(bucket => bucket.Projects.Select(project => project with { CurrentStage = $"{bucket.SubcategoryName} · {project.CurrentStage}" }))
                    .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                folded.Sum(bucket => bucket.OngoingCount),
                folded.Sum(bucket => bucket.CompletedCount),
                folded.Sum(bucket => bucket.CancelledCount));
        }
        // END SECTION

        // SECTION: CoE stage drill-down (ongoing projects grouped by current stage, workflow order; no stage = the chart's Unassigned bar)
        private static IReadOnlyList<AnalyticsBoardGroupVm> BuildCoeStageBoard(IReadOnlyList<CoeSubcategoryProjectsVm> buckets)
        {
            var ongoing = buckets
                .SelectMany(bucket => bucket.Projects.Select(project => (bucket.SubcategoryName, Project: project)))
                .Where(item => item.Project.LifecycleStatus == "Ongoing")
                .Select(item => (item.SubcategoryName, item.Project, StageCode: string.IsNullOrWhiteSpace(item.Project.CurrentStageCode) ? UnassignedStageCode : item.Project.CurrentStageCode!))
                .ToList();

            var order = AnalyticsAggregation.BuildStageOrderLookup(
                AnalyticsAggregation.BuildOrderedStageCodes(ongoing.Select(item => item.StageCode)));

            return ongoing
                .GroupBy(item => item.StageCode, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => AnalyticsAggregation.ResolveStageOrder(group.Key, order))
                .Select(group => new AnalyticsBoardGroupVm(
                    group.Key,
                    string.Equals(group.Key, UnassignedStageCode, StringComparison.OrdinalIgnoreCase) ? UnassignedStageName : group.First().Project.CurrentStage,
                    group.Count(),
                    group
                        .OrderBy(item => item.Project.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(item => new AnalyticsBoardProjectVm(item.Project.Id, item.Project.Name, item.SubcategoryName))
                        .ToList()))
                .ToList();
        }
        // END SECTION

        // SECTION: CoE category resolution (a category is CoE when it or any ancestor carries a CoE keyword)
        private async Task<List<int>> LoadCoeCategoryIdsAsync(CancellationToken cancellationToken)
        {
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .Select(c => new CoeCategoryDescriptor(c.Id, c.ParentId, c.Name))
                .ToListAsync(cancellationToken);

            if (categories.Count == 0)
            {
                return new List<int>();
            }

            var descriptorLookup = categories.ToDictionary(c => c.Id);
            return categories
                .Where(descriptor => IsCoeCategory(descriptor, descriptorLookup))
                .Select(descriptor => descriptor.Id)
                .ToList();
        }

        private static bool IsCoeCategory(
            CoeCategoryDescriptor descriptor,
            IReadOnlyDictionary<int, CoeCategoryDescriptor> lookup)
        {
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
        }

        private static bool MatchesCoeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return CoeCategoryKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
        // END SECTION

        private static CoeAnalyticsVm BuildEmptyCoeAnalyticsVm(IReadOnlyList<AnalyticsBoardGroupVm> arVrOutsideCoe) => new()
        {
            ByStage = Array.Empty<CoeStageBucketVm>(),
            SubcategoriesByLifecycle = Array.Empty<CoeSubcategoryLifecycleVm>(),
            SubcategoryProjects = Array.Empty<CoeSubcategoryProjectsVm>(),
            TotalCoeProjects = 0,
            ArVrOutsideCoe = arVrOutsideCoe
        };

        private static string NormalizeCoeSubcategoryName(string? name) =>
            string.IsNullOrWhiteSpace(name) ? DefaultCoeSubcategoryName : name.Trim();

        private static string FormatCoeLifecycleStatus(ProjectLifecycleStatus status) => status switch
        {
            ProjectLifecycleStatus.Active => "Ongoing",
            ProjectLifecycleStatus.Completed => "Completed",
            ProjectLifecycleStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };
        // END SECTION

        // SECTION: Shared helpers
        private async Task<IReadOnlyDictionary<int, ProjectCategoryHierarchyNode>> LoadCategoryHierarchyAsync(CancellationToken cancellationToken)
        {
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .Select(c => new { c.Id, c.Name, c.ParentId })
                .ToListAsync(cancellationToken);

            return categories.ToDictionary(
                category => category.Id,
                category => new ProjectCategoryHierarchyNode(category.Id, category.Name, category.ParentId));
        }

        private PresentStageSnapshot ResolvePresentStage(ProjectStageSnapshot project, DateOnly referenceDateIst)
        {
            var stages = project.Stages
                .Select(stage => new ProjectStageStatusSnapshot(
                    stage.StageCode,
                    stage.Status,
                    stage.SortOrder,
                    stage.ActualStart,
                    stage.CompletedOn))
                .ToList();

            // Measured against the IST "as of" day, the same day the page prints; the helper's own default is UTC.
            return PresentStageHelper.ComputePresentStageAndAge(
                stages,
                _workflowStageMetadataProvider,
                project.WorkflowVersion,
                project.Status,
                referenceDateIst);
        }

        private static string ResolveName(int? id, IReadOnlyDictionary<int, string> lookup) =>
            id.HasValue && lookup.TryGetValue(id.Value, out var name) ? name : "Uncategorized";

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

        private sealed record CompletedBoardRow(int Id, string? Name, int? CategoryId, string? CategoryName, int? TechnicalCategoryId, string? TechnicalName, int? Year);

        private sealed record CoeCategoryDescriptor(int Id, int? ParentId, string Name);

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
    }
}
