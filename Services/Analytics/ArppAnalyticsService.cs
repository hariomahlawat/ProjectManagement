using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Reports.ArppFyProjectUpdate;
using ProjectManagement.Services.Stages;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Analytics
{
    /// <summary>
    /// Analytics → ARPP: the projects listed in one financial year's published ARPP (the library's
    /// current position) joined to where each project stands today. Owns the business rules the page
    /// shows: which year to open on, IPA cost bands, standing by lifecycle, and milestone eligibility
    /// (past AoN, supply order placed, and the split of the rest into AoN to SO and Pre-AoN).
    /// </summary>
    public interface IArppAnalyticsService
    {
        /// <param name="requestedFinancialYearStart">The year the user asked for, or null for the default rule.</param>
        /// <param name="today">The "as of" date (IST) that decides the current financial year.</param>
        Task<ArppAnalyticsVm> BuildAsync(int? requestedFinancialYearStart, DateOnly today, CancellationToken cancellationToken = default);
    }

    public sealed class ArppAnalyticsService : IArppAnalyticsService
    {
        private const string UnassignedStageCode = "UNASSIGNED";
        private const string UnassignedStageName = "Unassigned";
        private const string NotKnownCode = "NOT_KNOWN";

        private readonly ApplicationDbContext _db;
        private readonly IArppLibraryService _arppLibrary;
        private readonly IArppFyProjectUpdateService _arppFyUpdate;
        private readonly IProjectPositionAsOfService? _positionAsOf;

        public ArppAnalyticsService(
            ApplicationDbContext db,
            IArppLibraryService arppLibrary,
            IArppFyProjectUpdateService arppFyUpdate,
            IProjectPositionAsOfService? positionAsOf = null)
        {
            _db = db;
            _arppLibrary = arppLibrary;
            _arppFyUpdate = arppFyUpdate;
            _positionAsOf = positionAsOf;
        }

        public async Task<ArppAnalyticsVm> BuildAsync(int? requestedFinancialYearStart, DateOnly today, CancellationToken cancellationToken = default)
        {
            var available = await _arppFyUpdate.GetAvailableFinancialYearsAsync(cancellationToken);
            var currentFy = FinancialYearHelper.GetStartYear(today);
            if (available.Count == 0)
            {
                return new ArppAnalyticsVm
                {
                    FinancialYearStart = currentFy,
                    FinancialYearDisplay = FinancialYearHelper.Format(currentFy),
                    IsCurrentFinancialYear = true
                };
            }

            var requested = requestedFinancialYearStart;
            var selected = requested.HasValue && available.Contains(requested.Value)
                ? requested.Value
                : available.Contains(currentFy) ? currentFy : available[0];
            string? fallbackNote = !requested.HasValue && selected != currentFy
                ? $"FY {FinancialYearHelper.Format(currentFy)} has no published ARPP yet; showing FY {FinancialYearHelper.Format(selected)}."
                : null;

            var position = await _arppLibrary.GetCurrentPositionAsync(selected, null, cancellationToken);
            var report = await _arppFyUpdate.BuildAsync(selected, cancellationToken);
            if (position is null)
            {
                return new ArppAnalyticsVm
                {
                    FinancialYearStart = selected,
                    FinancialYearDisplay = FinancialYearHelper.Format(selected),
                    AvailableFinancialYears = available,
                    IsCurrentFinancialYear = selected == currentFy,
                    FallbackNote = fallbackNote
                };
            }

            var standing = (report?.Rows ?? Array.Empty<ArppFyProjectUpdateRow>()).ToDictionary(row => row.ProjectId);
            var approved = position.ApprovedRows;
            var approvedIds = approved.Select(row => row.ProjectId).ToList();

            // Parent category per listed project, through the category hierarchy.
            var hierarchy = await LoadCategoryHierarchyAsync(cancellationToken);
            var categoryIds = await _db.Projects
                .AsNoTracking()
                .Where(project => approvedIds.Contains(project.Id))
                .Select(project => new { project.Id, project.CategoryId })
                .ToListAsync(cancellationToken);
            var parentCategoryByProject = categoryIds.ToDictionary(
                row => row.Id,
                row => ProjectCategoryHierarchyResolver.ResolveRoot(row.CategoryId, hierarchy)?.Name ?? "Uncategorized");

            static AnalyticsBoardProjectVm ToBoardProject(ArppLibraryCurrentRow row, string? detail = null) =>
                new(row.ProjectId, row.ProjectName, detail);

            IReadOnlyList<ArppValueGroupVm> GroupBy(Func<ArppLibraryCurrentRow, string> keyOf, Func<ArppLibraryCurrentRow, string>? nameOf = null, IComparer<string>? order = null)
            {
                var groups = approved
                    .GroupBy(row => keyOf(row))
                    .Select(group => new ArppValueGroupVm(
                        group.Key,
                        (nameOf ?? keyOf)(group.First()),
                        group.Count(),
                        group.Sum(row => row.IpaCost),
                        group.OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
                            .Select(row => ToBoardProject(row, IndianCurrencyFormatter.FormatCompact(row.IpaCost)))
                            .ToList()));
                return (order is null
                        ? groups.OrderByDescending(group => group.Count).ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                        : groups.OrderBy(group => group.Key, order))
                    .ToList();
            }

            var categoryOrder = Comparer<string>.Create((a, b) => CategoryRank(a).CompareTo(CategoryRank(b)));
            var byCategory = GroupBy(row => row.Category.ToString(), row => ArppDisplayNames.For(row.Category), categoryOrder);
            var byCfa = GroupBy(row => Trimmed(row.Cfa));
            var byFund = GroupBy(row => Trimmed(row.Fund));
            var byDfpds = GroupBy(row => Trimmed(row.DfpdsSchedule));
            var bandOrder = Comparer<string>.Create((a, b) => BandRank(a).CompareTo(BandRank(b)));
            var byCostBand = GroupBy(row => CostBandKey(row.IpaCost), row => CostBandName(CostBandKey(row.IpaCost)), bandOrder);
            var byParentCategory = GroupBy(row => parentCategoryByProject.TryGetValue(row.ProjectId, out var name) ? name : "Uncategorized");

            // A past year shows where its projects stood on 31 March, the last day of that financial year
            // (owner, 6 Nov 2026); the current year shows today. Either way each listed project gets one
            // Standing, and every card, list and chart below is counted from those.
            var fyEnd = FinancialYearHelper.GetEndDate(selected);
            DateOnly? asOf = fyEnd < today && _positionAsOf is not null ? fyEnd : null;
            var standings = asOf is { } yearEnd
                ? await StandingsOnAsync(approved, standing, yearEnd, cancellationToken)
                : await StandingsTodayAsync(approved, standing, approvedIds, cancellationToken);
            var asOfLabel = asOf is { } shownOn ? $"on {shownOn:d MMM yyyy}" : null;

            static AnalyticsBoardProjectVm ToStandingProject(Standing item) => new(item.ProjectId, item.ProjectName, item.Detail);
            static IOrderedEnumerable<Standing> ByName(IEnumerable<Standing> items) => items.OrderBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase);

            var completed = standings.Where(item => item.Lifecycle == LifecycleAsOf.Completed).ToList();
            var cancelled = standings.Where(item => item.Lifecycle == LifecycleAsOf.Cancelled).ToList();
            var ongoing = standings.Where(item => item.Lifecycle == LifecycleAsOf.Active).ToList();
            var lifecycleNotKnown = standings.Where(item => item.Lifecycle == LifecycleAsOf.NotKnown).ToList();

            var stageOfProject = ongoing.ToDictionary(item => item.ProjectId, item => (Code: item.StageCode, Name: item.StageName));
            var stageCodes = AnalyticsAggregation.BuildOrderedStageCodes(stageOfProject.Values.Select(stage => stage.Code));
            var stageOrder = AnalyticsAggregation.BuildStageOrderLookup(stageCodes);
            var byStage = StageCodes.All
                .Select(code => new ArppStageCountVm(code, StageCodes.DisplayNameOf(code), stageOfProject.Values.Count(stage => string.Equals(stage.Code, code, StringComparison.OrdinalIgnoreCase))))
                .Concat(stageCodes
                    .Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(code => code == NotKnownCode ? 1 : 0)
                    .Select(code => new ArppStageCountVm(code, stageOfProject.Values.First(stage => stage.Code == code).Name, stageOfProject.Values.Count(stage => stage.Code == code))))
                .ToList();

            var standingBoard = new List<AnalyticsBoardGroupVm>();
            if (completed.Count > 0)
            {
                standingBoard.Add(new AnalyticsBoardGroupVm("COMPLETED", "Completed", completed.Count, ByName(completed).Select(ToStandingProject).ToList()));
            }
            standingBoard.AddRange(ongoing
                .Where(item => item.StageCode != NotKnownCode)
                .GroupBy(item => item.StageCode, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => AnalyticsAggregation.ResolveStageOrder(group.Key, stageOrder))
                .Select(group => new AnalyticsBoardGroupVm(group.Key, group.First().StageName, group.Count(), ByName(group).Select(ToStandingProject).ToList())));
            var stageNotKnown = ongoing.Where(item => item.StageCode == NotKnownCode).Concat(lifecycleNotKnown).ToList();
            if (stageNotKnown.Count > 0)
            {
                standingBoard.Add(new AnalyticsBoardGroupVm(NotKnownCode, $"Not known {asOfLabel}", stageNotKnown.Count, ByName(stageNotKnown).Select(ToStandingProject).ToList()));
            }
            if (cancelled.Count > 0)
            {
                standingBoard.Add(new AnalyticsBoardGroupVm("CANCELLED", "Cancelled", cancelled.Count, ByName(cancelled).Select(ToStandingProject).ToList()));
            }

            // Cards and milestones. Supply orders count projects still in work; completed projects carry
            // their own supply-order value. The rest split by AoN; a project the records cannot place in one
            // card is counted as not known, never guessed.
            var pastAon = standings.Where(item => item.PastAon == true).ToList();
            var withSo = ongoing.Where(item => item.InWork && item.SupplyOrderPlaced == true).ToList();
            var beforeSo = ongoing.Where(item => item.InWork && item.SupplyOrderPlaced != true).ToList();
            var preAon = beforeSo.Where(item => item.PastAon == false).ToList();
            var aonToSo = beforeSo.Where(item => item.PastAon == true && item.SupplyOrderPlaced == false).ToList();
            var cardNotKnown = beforeSo.Count - preAon.Count - aonToSo.Count + lifecycleNotKnown.Count;

            static AnalyticsBoardGroupVm Milestone(string key, string name, IEnumerable<Standing> rows, Func<Standing, string?> detail) =>
                new(key, name, rows.Count(), ByName(rows).Select(item => new AnalyticsBoardProjectVm(item.ProjectId, item.ProjectName, detail(item))).ToList());

            var milestoneBoard = new[]
            {
                Milestone("AON", asOfLabel is null ? "Past AoN" : $"Past AoN {asOfLabel}", pastAon, item => item.AonDetail),
                Milestone("SO", asOfLabel is null ? "Supply order placed" : $"Supply order placed {asOfLabel}", withSo, item => item.SupplyOrderDetail)
            };

            return new ArppAnalyticsVm
            {
                FinancialYearStart = selected,
                FinancialYearDisplay = FinancialYearHelper.Format(selected),
                AvailableFinancialYears = available,
                IsCurrentFinancialYear = selected == currentFy,
                AsOfDate = asOf,
                FallbackNote = fallbackNote,
                ListedCount = approved.Count,
                ListedIpaValue = position.ApprovedIpaValue,
                NewCount = approved.Count(row => row.Category == ArppCategory.New),
                CommittedLiabilityCount = approved.Count(row => row.Category == ArppCategory.CommittedLiability),
                CarryForwardCount = approved.Count(row => row.Category == ArppCategory.CarryForward),
                DelistedCount = position.DelistedRows.Count,
                DelistedIpaValue = position.DelistedIpaValue,
                AddedByAddendumCount = approved.Count(row => row.SourceKind == ArppIssueKind.Addendum),
                UnlinkedRowCount = position.TotalUnlinkedDocumentRows,
                OngoingCount = ongoing.Count,
                CompletedCount = completed.Count,
                CancelledCount = cancelled.Count,
                NotKnownCount = lifecycleNotKnown.Count,
                ByStage = byStage,
                StandingBoard = standingBoard,
                ByCategory = byCategory,
                ByCfa = byCfa,
                ByFund = byFund,
                ByDfpds = byDfpds,
                ByCostBand = byCostBand,
                ByParentCategory = byParentCategory,
                PastAonCount = pastAon.Count,
                SupplyOrderCount = withSo.Count,
                SupplyOrderValue = withSo.Sum(item => item.SupplyOrderValue ?? 0m),
                CompletedSupplyOrderValue = completed.Sum(item => item.SupplyOrderValue ?? 0m),
                BeforeSupplyOrderCount = beforeSo.Count,
                AonToSupplyOrderCount = aonToSo.Count,
                PreAonCount = preAon.Count,
                CardNotKnownCount = cardNotKnown,
                MilestoneBoard = milestoneBoard
            };
        }

        /// <summary>One listed project as the page counts it: today, or on a past year's 31 March.</summary>
        private sealed record Standing(
            int ProjectId,
            string ProjectName,
            LifecycleAsOf Lifecycle,
            bool InWork,
            string StageCode,
            string StageName,
            string? Detail,
            bool? PastAon,
            string? AonDetail,
            bool? SupplyOrderPlaced,
            decimal? SupplyOrderValue,
            string? SupplyOrderDetail);

        private static string? SupplyOrderDetail(decimal? amount, DateOnly? date) =>
            amount is > 0m ? IndianCurrencyFormatter.FormatCompact(amount.Value) : date?.ToString("d MMM yyyy");

        // Today: the lifecycle status from the FY update rows is authoritative (an archived project keeps
        // its Completed or Cancelled status); the published row's display status is only the fallback.
        private async Task<IReadOnlyList<Standing>> StandingsTodayAsync(
            IReadOnlyList<ArppLibraryCurrentRow> approved,
            IReadOnlyDictionary<int, ArppFyProjectUpdateRow> standing,
            IReadOnlyList<int> approvedIds,
            CancellationToken cancellationToken)
        {
            // Past AoN is the AoN stage marked Completed, with or without its date (owner, 6 Nov 2026), or
            // Skipped (AoN not required: the project is past it). A project whose workflow has no AoN stage
            // is past AoN once its current stage is beyond the Pre-AoN phase.
            var aonStatuses = (await _db.ProjectStages
                    .AsNoTracking()
                    .Where(stage => approvedIds.Contains(stage.ProjectId) && stage.StageCode == StageCodes.AON)
                    .Select(stage => new { stage.ProjectId, stage.Status })
                    .ToListAsync(cancellationToken))
                .GroupBy(stage => stage.ProjectId)
                .ToDictionary(group => group.Key, group => group.Select(stage => stage.Status).ToList());
            bool IsAonSkipped(ArppFyProjectUpdateRow row) =>
                !row.AonDate.HasValue
                && aonStatuses.TryGetValue(row.ProjectId, out var statuses)
                && !statuses.Contains(StageStatus.Completed)
                && statuses.Contains(StageStatus.Skipped);
            bool IsPastAon(ArppFyProjectUpdateRow row)
            {
                if (row.AonDate.HasValue)
                {
                    return true;
                }

                if (aonStatuses.TryGetValue(row.ProjectId, out var statuses))
                {
                    return statuses.Contains(StageStatus.Completed) || statuses.Contains(StageStatus.Skipped);
                }

                return IsInWork(row)
                    && !string.IsNullOrWhiteSpace(row.CurrentStageCode)
                    && StagePhases.Of(row.CurrentStageCode) != StagePhase.PreAon;
            }

            var result = new List<Standing>(approved.Count);
            foreach (var row in approved)
            {
                standing.TryGetValue(row.ProjectId, out var update);
                var lifecycle = update?.LifecycleStatus ?? row.ProjectStatus switch
                {
                    "Completed" => ProjectLifecycleStatus.Completed,
                    "Cancelled" => ProjectLifecycleStatus.Cancelled,
                    _ => ProjectLifecycleStatus.Active
                };
                var archived = update?.IsArchived ?? row.ProjectStatus == "Archived";
                var detail = archived ? $"{ArppDisplayNames.For(row.Category)} · archived" : ArppDisplayNames.For(row.Category);
                var (code, name) = update is not null && !string.IsNullOrWhiteSpace(update.CurrentStageCode)
                    ? (update.CurrentStageCode!.Trim(), update.CurrentStageLabel)
                    : (UnassignedStageCode, UnassignedStageName);
                var inWork = update is not null && IsInWork(update);
                result.Add(new Standing(
                    row.ProjectId,
                    row.ProjectName,
                    lifecycle switch
                    {
                        ProjectLifecycleStatus.Completed => LifecycleAsOf.Completed,
                        ProjectLifecycleStatus.Cancelled => LifecycleAsOf.Cancelled,
                        _ => LifecycleAsOf.Active
                    },
                    inWork,
                    code,
                    name,
                    detail,
                    update is null ? null : IsPastAon(update),
                    update is null ? null : update.AonDate?.ToString("d MMM yyyy") ?? (IsAonSkipped(update) ? "AoN skipped" : "date not recorded"),
                    update is null ? null : inWork && HasSupplyOrderEvidence(update),
                    update?.SupplyOrderAmountInRupees,
                    update is null ? null : SupplyOrderDetail(update.SupplyOrderAmountInRupees, update.SupplyOrderDate)));
            }

            return result;
        }

        // On a past year's 31 March, from today's records (IProjectPositionAsOfService). Supply-order values
        // are today's (a placed order's PNC/L1 value does not change); they count once the order was placed.
        private async Task<IReadOnlyList<Standing>> StandingsOnAsync(
            IReadOnlyList<ArppLibraryCurrentRow> approved,
            IReadOnlyDictionary<int, ArppFyProjectUpdateRow> standing,
            DateOnly asOf,
            CancellationToken cancellationToken)
        {
            var positions = await _positionAsOf!.ResolveAsync(approved.Select(row => row.ProjectId).ToList(), asOf, cancellationToken);
            var result = new List<Standing>(approved.Count);
            foreach (var row in approved)
            {
                standing.TryGetValue(row.ProjectId, out var update);
                var category = ArppDisplayNames.For(row.Category);
                if (!positions.TryGetValue(row.ProjectId, out var at))
                {
                    result.Add(new Standing(row.ProjectId, row.ProjectName, LifecycleAsOf.NotKnown, false, NotKnownCode, "Not known", $"{category} · no project record", null, null, null, null, null));
                    continue;
                }

                var value = update?.SupplyOrderAmountInRupees;
                var (code, name, detail) = at.Lifecycle switch
                {
                    LifecycleAsOf.NotKnown => (NotKnownCode, "Not known", $"{category} · completion date not recorded"),
                    LifecycleAsOf.Active when at.Stage.IsKnown && at.Stage.StageCode is { } known => (known, at.Stage.StageName, category),
                    LifecycleAsOf.Active when at.Stage.IsKnown => (UnassignedStageCode, "Workflow concluded", category),
                    LifecycleAsOf.Active => (NotKnownCode, "Not known", $"{category} · {at.Stage.StageName} (dates not recorded)"),
                    _ => (UnassignedStageCode, UnassignedStageName, category)
                };
                result.Add(new Standing(
                    row.ProjectId,
                    row.ProjectName,
                    at.Lifecycle,
                    at.Lifecycle == LifecycleAsOf.Active,
                    code,
                    name,
                    detail,
                    at.PastAon,
                    at.AonDate?.ToString("d MMM yyyy") ?? (at.AonSkipped ? "AoN skipped" : "date not recorded"),
                    at.Lifecycle == LifecycleAsOf.Active ? at.SupplyOrderPlaced : null,
                    value,
                    SupplyOrderDetail(value, at.SupplyOrderDate)));
            }

            return result;
        }

        private static bool IsInWork(ArppFyProjectUpdateRow row) =>
            row.LifecycleStatus == ProjectLifecycleStatus.Active && !row.IsArchived;

        private static bool HasSupplyOrderEvidence(ArppFyProjectUpdateRow row)
        {
            if (row.SupplyOrderDate.HasValue)
            {
                return true;
            }

            // Lower maturity numbers are further along; being *in* the Supply Order stage is not yet an order.
            var maturity = ProjectStageMaturityOrder.Resolve(row.LifecycleStatus, row.CurrentStageCode);
            return maturity != ProjectStageMaturityOrder.Unknown && maturity < ProjectStageMaturityOrder.SupplyOrder;
        }

        private static string Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();

        private static int CategoryRank(string key) => key switch
        {
            nameof(ArppCategory.New) => 0,
            nameof(ArppCategory.CommittedLiability) => 1,
            nameof(ArppCategory.CarryForward) => 2,
            _ => 3
        };

        // IPA cost bands in rupees: under 1 crore, 1 to 5 crore, 5 to 20 crore, 20 crore and above.
        private static string CostBandKey(decimal rupees) => rupees switch
        {
            < 10_000_000m => "lt1",
            < 50_000_000m => "1to5",
            < 200_000_000m => "5to20",
            _ => "ge20"
        };

        private static string CostBandName(string key) => key switch
        {
            "lt1" => "Under \u20b91 Cr",
            "1to5" => "\u20b91 to 5 Cr",
            "5to20" => "\u20b95 to 20 Cr",
            _ => "\u20b920 Cr and above"
        };

        private static int BandRank(string key) => key switch { "lt1" => 0, "1to5" => 1, "5to20" => 2, _ => 3 };
        // END SECTION


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
    }
}
