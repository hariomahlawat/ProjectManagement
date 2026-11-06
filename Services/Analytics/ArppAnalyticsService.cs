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

        private readonly ApplicationDbContext _db;
        private readonly IArppLibraryService _arppLibrary;
        private readonly IArppFyProjectUpdateService _arppFyUpdate;

        public ArppAnalyticsService(
            ApplicationDbContext db,
            IArppLibraryService arppLibrary,
            IArppFyProjectUpdateService arppFyUpdate)
        {
            _db = db;
            _arppLibrary = arppLibrary;
            _arppFyUpdate = arppFyUpdate;
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

            // Standing: the lifecycle status from the FY update rows is authoritative (an archived project keeps
            // its Completed or Cancelled status); the published row's display status is only the fallback.
            ProjectLifecycleStatus LifecycleOf(ArppLibraryCurrentRow row) =>
                standing.TryGetValue(row.ProjectId, out var update)
                    ? update.LifecycleStatus
                    : row.ProjectStatus switch
                    {
                        "Completed" => ProjectLifecycleStatus.Completed,
                        "Cancelled" => ProjectLifecycleStatus.Cancelled,
                        _ => ProjectLifecycleStatus.Active
                    };
            string? StandingDetail(ArppLibraryCurrentRow row)
            {
                var archived = standing.TryGetValue(row.ProjectId, out var update) ? update.IsArchived : row.ProjectStatus == "Archived";
                return archived ? $"{ArppDisplayNames.For(row.Category)} · archived" : ArppDisplayNames.For(row.Category);
            }

            var completedRows = approved.Where(row => LifecycleOf(row) == ProjectLifecycleStatus.Completed).ToList();
            var cancelledRows = approved.Where(row => LifecycleOf(row) == ProjectLifecycleStatus.Cancelled).ToList();
            var ongoingRows = approved.Where(row => LifecycleOf(row) == ProjectLifecycleStatus.Active).ToList();

            var stageOfProject = ongoingRows.ToDictionary(
                row => row.ProjectId,
                row => standing.TryGetValue(row.ProjectId, out var update) && !string.IsNullOrWhiteSpace(update.CurrentStageCode)
                    ? (Code: update.CurrentStageCode!.Trim(), Name: update.CurrentStageLabel)
                    : (Code: UnassignedStageCode, Name: UnassignedStageName));
            var stageCodes = AnalyticsAggregation.BuildOrderedStageCodes(stageOfProject.Values.Select(stage => stage.Code));
            var stageOrder = AnalyticsAggregation.BuildStageOrderLookup(stageCodes);
            var byStage = StageCodes.All
                .Select(code => new ArppStageCountVm(code, StageCodes.DisplayNameOf(code), stageOfProject.Values.Count(stage => string.Equals(stage.Code, code, StringComparison.OrdinalIgnoreCase))))
                .Concat(stageCodes
                    .Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase))
                    .Select(code => new ArppStageCountVm(code, stageOfProject.Values.First(stage => stage.Code == code).Name, stageOfProject.Values.Count(stage => stage.Code == code))))
                .ToList();

            var standingBoard = new List<AnalyticsBoardGroupVm>();
            if (completedRows.Count > 0)
            {
                standingBoard.Add(new AnalyticsBoardGroupVm("COMPLETED", "Completed", completedRows.Count,
                    completedRows.OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase).Select(row => ToBoardProject(row, StandingDetail(row))).ToList()));
            }
            standingBoard.AddRange(ongoingRows
                .GroupBy(row => stageOfProject[row.ProjectId].Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => AnalyticsAggregation.ResolveStageOrder(group.Key, stageOrder))
                .Select(group => new AnalyticsBoardGroupVm(
                    group.Key,
                    stageOfProject[group.First().ProjectId].Name,
                    group.Count(),
                    group.OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase).Select(row => ToBoardProject(row, StandingDetail(row))).ToList())));
            if (cancelledRows.Count > 0)
            {
                standingBoard.Add(new AnalyticsBoardGroupVm("CANCELLED", "Cancelled", cancelledRows.Count,
                    cancelledRows.OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase).Select(row => ToBoardProject(row, StandingDetail(row))).ToList()));
            }

            // Milestones from the FY update rows (approved projects only).
            var updates = approvedIds.Where(standing.ContainsKey).Select(id => standing[id]).ToList();

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

            var pastAon = updates.Where(IsPastAon).ToList();
            // "Supply order placed" is a milestone of projects still in work: a supply-order date, or the
            // project having reached the Supply Order stage or beyond. A resolved amount alone can be an L1
            // or PNC cost recorded before any order exists. Completed projects are not in this count; their
            // supply-order value is reported on its own (CompletedSupplyOrderValue).
            var withSo = updates.Where(row => IsInWork(row) && HasSupplyOrderEvidence(row)).ToList();
            var completedUpdates = updates.Where(row => row.LifecycleStatus == ProjectLifecycleStatus.Completed).ToList();
            // The rest: ongoing listed projects that have not reached a supply order, split by AoN: past it
            // ("AoN to SO stage") or not yet (Pre-AoN: FS, IPA, SoW, and the AoN stage until it is completed).
            // Cancelled projects are in no card. The ARPP page carries no PDC figures (owner, 6 Nov 2026).
            var beforeSo = updates.Where(row => IsInWork(row) && !HasSupplyOrderEvidence(row)).ToList();
            var aonToSo = beforeSo.Where(IsPastAon).ToList();
            var preAon = beforeSo.Where(row => !IsPastAon(row)).ToList();

            static AnalyticsBoardGroupVm Milestone(string key, string name, IReadOnlyList<ArppFyProjectUpdateRow> rows, Func<ArppFyProjectUpdateRow, string?> detail) =>
                new(key, name, rows.Count, rows.OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase).Select(row => new AnalyticsBoardProjectVm(row.ProjectId, row.ProjectName, detail(row))).ToList());

            var milestoneBoard = new[]
            {
                Milestone("AON", "Past AoN", pastAon, row => row.AonDate?.ToString("d MMM yyyy") ?? (IsAonSkipped(row) ? "AoN skipped" : "date not recorded")),
                Milestone("SO", "Supply order placed", withSo, row => row.SupplyOrderAmountInRupees is > 0m ? IndianCurrencyFormatter.FormatCompact(row.SupplyOrderAmountInRupees.Value) : row.SupplyOrderDate?.ToString("d MMM yyyy"))
            };

            return new ArppAnalyticsVm
            {
                FinancialYearStart = selected,
                FinancialYearDisplay = FinancialYearHelper.Format(selected),
                AvailableFinancialYears = available,
                IsCurrentFinancialYear = selected == currentFy,
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
                OngoingCount = ongoingRows.Count,
                CompletedCount = completedRows.Count,
                CancelledCount = cancelledRows.Count,
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
                SupplyOrderValue = withSo.Sum(row => row.SupplyOrderAmountInRupees ?? 0m),
                CompletedSupplyOrderValue = completedUpdates.Sum(row => row.SupplyOrderAmountInRupees ?? 0m),
                BeforeSupplyOrderCount = beforeSo.Count,
                AonToSupplyOrderCount = aonToSo.Count,
                PreAonCount = preAon.Count,
                MilestoneBoard = milestoneBoard
            };
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
