using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Analytics
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private CoeAnalyticsVm? _cachedCoeAnalytics;

        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        private static readonly ProjectLifecycleFilter[] LifecycleFilters =
        {
            ProjectLifecycleFilter.Active,
            ProjectLifecycleFilter.Completed,
            ProjectLifecycleFilter.Cancelled,
            ProjectLifecycleFilter.All
        };

        public AnalyticsTab ActiveTab { get; private set; } = AnalyticsTab.Completed;

        public IReadOnlyList<CategoryOption> Categories { get; private set; } = Array.Empty<CategoryOption>();
        public IReadOnlyList<TechnicalCategoryOption> TechnicalCategories { get; private set; } = Array.Empty<TechnicalCategoryOption>();

        public int CompletedCount { get; private set; }
        public int OngoingCount { get; private set; }
        public int CoeCount { get; private set; }

        public CompletedAnalyticsVm? Completed { get; private set; }
        public OngoingAnalyticsVm? Ongoing { get; private set; }
        public CoeAnalyticsVm? Coe { get; private set; }

        public ProjectLifecycleFilter DefaultLifecycle => ProjectLifecycleFilter.Active;

        public int LifecycleViewCount => LifecycleFilters.Length;

        public int CategoryCount => Categories.Count;

        public int TechnicalGroupCount => TechnicalCategories.Count;

        public async Task OnGetAsync(string? tab, CancellationToken cancellationToken)
        {
            ActiveTab = tab?.ToLowerInvariant() switch
            {
                "ongoing" => AnalyticsTab.Ongoing,
                "coe" => AnalyticsTab.Coe,
                _ => AnalyticsTab.Completed
            };

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
                    Coe = _cachedCoeAnalytics ?? BuildCoeAnalyticsVm();
                    break;
            }
            // END SECTION
        }

        private async Task LoadAnalyticsAsync(CancellationToken cancellationToken)
        {
            Categories = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryOption(c.Id, c.Name))
                .ToListAsync(cancellationToken);

            TechnicalCategories = await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new TechnicalCategoryOption(c.Id, c.Name))
                .ToListAsync(cancellationToken);

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

            _cachedCoeAnalytics = BuildCoeAnalyticsVm();
            CoeCount = _cachedCoeAnalytics.ByLifecycleStatus.Sum(point => point.Value);
        }

        private async Task<CompletedAnalyticsVm> BuildCompletedAnalyticsAsync(CancellationToken cancellationToken)
        {
            // SECTION: Completed analytics aggregation
            var completedQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

            var byCategory = await BuildCategoryCountsAsync(completedQuery, cancellationToken);

            var byTechnical = await BuildTechnicalCategoryCountsAsync(completedQuery, cancellationToken);

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

            return new CompletedAnalyticsVm
            {
                ByCategory = byCategory,
                ByTechnical = byTechnical,
                PerYear = perYear,
                TotalCompletedProjects = await completedQuery.CountAsync(cancellationToken)
            };
            // END SECTION
        }

        private async Task<OngoingAnalyticsVm> BuildOngoingAnalyticsAsync(CancellationToken cancellationToken)
        {
            // SECTION: Ongoing analytics aggregation
            var ongoingQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active);

            var total = await ongoingQuery.CountAsync(cancellationToken);

            var byCategory = await BuildCategoryCountsAsync(ongoingQuery, cancellationToken);
            var byStage = await BuildOngoingStageDistributionAsync(cancellationToken);
            var stageDurations = await BuildOngoingStageDurationsAsync(cancellationToken);

            return new OngoingAnalyticsVm
            {
                TotalOngoingProjects = total,
                ByCategory = byCategory,
                ByStage = byStage,
                AvgStageDurations = stageDurations
            };
            // END SECTION
        }

        private static CoeAnalyticsVm BuildCoeAnalyticsVm()
        {
            // SECTION: Placeholder CoE analytics data (replace with live data wiring when available)
            var byStage = new List<LabelValuePoint>
            {
                new("Discovery", 4),
                new("Planning", 6),
                new("Execution", 3),
                new("Adoption", 2)
            };

            var byLifecycle = new List<LabelValuePoint>
            {
                new("Ongoing", 9),
                new("Completed", 5),
                new("Cancelled", 1)
            };

            var bySubcategoryLifecycle = new List<CoeSubcategoryLifecyclePoint>
            {
                new("AR/VR", "Ongoing", 3),
                new("AR/VR", "Completed", 1),
                new("AI", "Ongoing", 2),
                new("AI", "Completed", 2),
                new("AI", "Cancelled", 1),
                new("Drones", "Ongoing", 2),
                new("Drones", "Completed", 1),
                new("Robotics", "Ongoing", 1),
                new("Robotics", "Completed", 1)
            };

            var roadmap = new CoeRoadmapVm
            {
                ShortTerm = new[]
                {
                    "Publish CoE intake playbook",
                    "Stand up AR/VR showcase lab"
                },
                MidTerm = new[]
                {
                    "Expand AI PoC factory",
                    "Launch drone interoperability pilots"
                },
                LongTerm = new[]
                {
                    "Operationalise robotics center across regions",
                    "Establish shared evaluation metrics across CoEs"
                }
            };

            return new CoeAnalyticsVm
            {
                ByStage = byStage,
                ByLifecycleStatus = byLifecycle,
                BySubcategoryLifecycle = bySubcategoryLifecycle,
                Roadmap = roadmap
            };
            // END SECTION
        }

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
                    p.ProjectStages
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.StageCode)
                        .Select(s => new StageSnapshot(
                            s.StageCode,
                            s.Status,
                            s.SortOrder,
                            s.CompletedOn))
                        .ToList()))
                .ToListAsync(cancellationToken);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in stageSnapshots)
            {
                var stage = DetermineCurrentStage(project);
                if (stage is null || string.IsNullOrWhiteSpace(stage.StageCode))
                {
                    continue;
                }

                counts.TryGetValue(stage.StageCode, out var existing);
                counts[stage.StageCode] = existing + 1;
            }

            var orderedCodes = StageCodes.All
                .Where(code => counts.ContainsKey(code))
                .Concat(counts.Keys.Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return orderedCodes
                .Select(code => new AnalyticsStageCountPoint(StageCodes.DisplayNameOf(code), counts[code]))
                .ToList();
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
                    s.StageCode,
                    s.ActualStart,
                    s.CompletedOn
                })
                .ToListAsync(cancellationToken);

            return stageRows
                .Where(s => !string.IsNullOrWhiteSpace(s.StageCode) && s.ActualStart.HasValue)
                .GroupBy(s => s.StageCode!)
                .Select(g => new AnalyticsStageDurationPoint(
                    StageCodes.DisplayNameOf(g.Key),
                    g.Average(item => CalculateStageDurationDays(item.ActualStart, item.CompletedOn))))
                .OrderByDescending(x => x.Days)
                .ToList();
        }

        private static double CalculateStageDurationDays(DateOnly? start, DateOnly? end)
        {
            if (!start.HasValue)
            {
                return 0;
            }

            var startDate = start.Value.ToDateTime(TimeOnly.MinValue);
            var effectiveEnd = (end ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue);
            var duration = (effectiveEnd - startDate).TotalDays;
            return duration < 0 ? 0 : duration;
        }

        private static StageSnapshot? DetermineCurrentStage(ProjectStageSnapshot project)
        {
            var stages = project.Stages;
            if (stages.Count == 0)
            {
                return null;
            }

            if (project.Status == ProjectLifecycleStatus.Active)
            {
                var inProgress = stages
                    .Where(s => s.Status == StageStatus.InProgress)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.StageCode)
                    .FirstOrDefault();
                if (inProgress != null)
                {
                    return inProgress;
                }

                var notStarted = stages
                    .Where(s => s.Status == StageStatus.NotStarted)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.StageCode)
                    .FirstOrDefault();
                if (notStarted != null)
                {
                    return notStarted;
                }
            }

            var completed = stages
                .Where(s => s.Status == StageStatus.Completed)
                .OrderByDescending(s => s.CompletedOn ?? DateOnly.MinValue)
                .ThenByDescending(s => s.SortOrder)
                .FirstOrDefault();
            if (completed != null)
            {
                return completed;
            }

            return stages[0];
        }

        private static string ResolveName(int? id, IReadOnlyDictionary<int, string> lookup) =>
            id.HasValue && lookup.TryGetValue(id.Value, out var name)
                ? name
                : "Uncategorized";

        private sealed record StageSnapshot(
            string StageCode,
            StageStatus Status,
            int SortOrder,
            DateOnly? CompletedOn);

        private sealed record ProjectStageSnapshot(
            ProjectLifecycleStatus Status,
            IReadOnlyList<StageSnapshot> Stages);

        private sealed class CategoryAggregation
        {
            public int? Id { get; init; }
            public int Count { get; init; }
        }
        // END SECTION

        public sealed record CategoryOption(int Id, string Name);
        public sealed record TechnicalCategoryOption(int Id, string Name);
    }
}
