using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjectManagement.Areas.Dashboard.Components.ProjectPulse;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Dashboard;

public interface IProjectPulseService
{
    Task<ProjectPulseVm> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class ProjectPulseService : IProjectPulseService
{
    // SECTION: Constants & dependencies
    private const string CacheKey = "Dashboard:ProjectPulse";
    private const string CompletedPage = "/Projects/CompletedSummary/Index";
    private const string OngoingPage = "/Projects/Ongoing/Index";
    private const string RepositoryPage = "/Projects/Index";
    private const string AnalyticsPage = "/Analytics/Index";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private static readonly string[] StageOrder = StageCodes.All;

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    // END SECTION

    public ProjectPulseService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    // SECTION: Public API
    public Task<ProjectPulseVm> GetAsync(CancellationToken cancellationToken = default)
    {
        return _cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return BuildAsync(cancellationToken);
        })!;
    }
    // END SECTION

    // SECTION: Aggregation
    private async Task<ProjectPulseVm> BuildAsync(CancellationToken cancellationToken)
    {
        var baseQuery = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted);

        var total = await baseQuery.CountAsync(cancellationToken);
        var completed = await baseQuery
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .CountAsync(cancellationToken);
        var ongoing = await baseQuery
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active && !p.IsArchived)
            .CountAsync(cancellationToken);

        var completedByCategory = await BuildCompletedByCategoryAsync(cancellationToken);
        var ongoingByStage = await BuildOngoingByStageAsync(cancellationToken);
        var (technicalTop, remainingTechnical) = await BuildTechnicalCategorySeriesAsync(cancellationToken);
        var availableForProliferation = await CountProliferationEligibleAsync(cancellationToken);

        return new ProjectPulseVm
        {
            ProliferationEligible = availableForProliferation,
            AnalyticsUrl = AnalyticsPage,
            CompletedCount = completed,
            OngoingCount = ongoing,
            TotalProjects = total,
            CompletedByProjectCategory = completedByCategory,
            OngoingByStageOrdered = ongoingByStage,
            AllByTechnicalCategoryTop = technicalTop,
            RemainingTechCategories = remainingTechnical,
            CompletedUrl = CompletedPage,
            OngoingUrl = OngoingPage,
            RepositoryUrl = RepositoryPage
        };
    }
    // END SECTION

    // SECTION: Series builders
    private async Task<IReadOnlyList<CategorySlice>> BuildCompletedByCategoryAsync(CancellationToken cancellationToken)
    {
        var completedSeries = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .GroupBy(p => p.Category != null ? p.Category.Name : "Uncategorized")
            .Select(g => new CategorySlice(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var ordered = completedSeries
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return CapCompletedCategories(ordered);
    }

    private async Task<(IReadOnlyList<CategorySlice> TopSeries, int RemainingCount)> BuildTechnicalCategorySeriesAsync(CancellationToken cancellationToken)
    {
        var technicalSeries = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived)
            .GroupBy(p => p.TechnicalCategory != null ? p.TechnicalCategory.Name : "Unclassified")
            .Select(g => new CategorySlice(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var ordered = technicalSeries
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = ordered.Take(5).ToList();
        var remaining = ordered.Skip(5).Sum(x => x.Count);

        return (top, remaining);
    }

    private async Task<IReadOnlyList<StagePoint>> BuildOngoingByStageAsync(CancellationToken cancellationToken)
    {
        var snapshots = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Select(p => new StageProjectSnapshot(
                p.Id,
                p.ProjectStages
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.StageCode)
                    .Select(s => new StageState(s.StageCode, s.Status, s.SortOrder, s.CompletedOn))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in snapshots)
        {
            var stage = DetermineCurrentStage(project);
            if (stage is null || string.IsNullOrWhiteSpace(stage.StageCode))
            {
                continue;
            }

            counts.TryGetValue(stage.StageCode, out var existing);
            counts[stage.StageCode] = existing + 1;
        }

        return StageOrder
            .Select(code => new StagePoint(
                StageCodes.DisplayNameOf(code),
                counts.TryGetValue(code, out var value) ? value : 0))
            .ToList();
    }
    // END SECTION

    // SECTION: Proliferation counts
    private async Task<int> CountProliferationEligibleAsync(CancellationToken cancellationToken)
    {
        var count = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(ts => ts.AvailableForProliferation
                && ts.Project != null
                && ts.Project.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !ts.Project.IsDeleted
                && !ts.Project.IsArchived
                && (ts.Project.Tot == null || ts.Project.Tot.Status != ProjectTotStatus.InProgress))
            .CountAsync(cancellationToken);

        return count;
    }
    // END SECTION

    // SECTION: Stage helpers
    private static StageState? DetermineCurrentStage(StageProjectSnapshot snapshot)
    {
        if (snapshot.Stages.Count == 0)
        {
            return null;
        }

        var inProgress = snapshot.Stages
            .Where(s => s.Status == StageStatus.InProgress)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .FirstOrDefault();
        if (inProgress != null)
        {
            return inProgress;
        }

        var notStarted = snapshot.Stages
            .Where(s => s.Status == StageStatus.NotStarted)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .FirstOrDefault();
        if (notStarted != null)
        {
            return notStarted;
        }

        var completed = snapshot.Stages
            .Where(s => s.Status == StageStatus.Completed)
            .OrderByDescending(s => s.CompletedOn ?? DateOnly.MinValue)
            .ThenByDescending(s => s.SortOrder)
            .FirstOrDefault();
        if (completed != null)
        {
            return completed;
        }

        return snapshot.Stages[0];
    }

    private static IReadOnlyList<CategorySlice> CapCompletedCategories(IReadOnlyList<CategorySlice> ordered)
    {
        const int MaxSlices = 6;
        if (ordered.Count <= MaxSlices)
        {
            return ordered;
        }

        var slices = ordered.Take(MaxSlices - 1).ToList();
        var otherCount = ordered.Skip(MaxSlices - 1).Sum(slice => slice.Count);
        slices.Add(new CategorySlice("Other", otherCount));
        return slices;
    }

    private sealed record StageProjectSnapshot(int ProjectId, IReadOnlyList<StageState> Stages);

    private sealed record StageState(string StageCode, StageStatus Status, int SortOrder, DateOnly? CompletedOn);
    // END SECTION
}
