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
        var idle = Math.Max(0, total - completed - ongoing);

        var completedByCategory = await BuildCompletedByCategoryAsync(cancellationToken);
        var ongoingByStage = await BuildOngoingByStageAsync(cancellationToken);
        var technicalCategorySeries = await BuildTechnicalCategorySeriesAsync(cancellationToken);
        var availableForProliferation = await CountProliferationEligibleAsync(cancellationToken);

        return new ProjectPulseVm
        {
            TotalProjects = total,
            CompletedCount = completed,
            OngoingCount = ongoing,
            IdleCount = idle,
            AvailableForProliferationCount = availableForProliferation,
            CompletedByProjectCategory = completedByCategory,
            OngoingByStage = ongoingByStage,
            AllByTechnicalCategory = technicalCategorySeries
        };
    }
    // END SECTION

    // SECTION: Series builders
    private async Task<IReadOnlyList<LabelValue>> BuildCompletedByCategoryAsync(CancellationToken cancellationToken)
    {
        var completedSeries = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .GroupBy(p => p.Category != null ? p.Category.Name : "Uncategorized")
            .Select(g => new
            {
                Label = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label)
            .Select(x => new LabelValue(x.Label, x.Count))
            .ToListAsync(cancellationToken);

        return completedSeries;
    }

    private async Task<IReadOnlyList<LabelValue>> BuildTechnicalCategorySeriesAsync(CancellationToken cancellationToken)
    {
        var technicalSeries = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived)
            .GroupBy(p => p.TechnicalCategory != null ? p.TechnicalCategory.Name : "Unclassified")
            .Select(g => new
            {
                Label = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label)
            .Select(x => new LabelValue(x.Label, x.Count))
            .ToListAsync(cancellationToken);

        return technicalSeries;
    }

    private async Task<IReadOnlyList<LabelValue>> BuildOngoingByStageAsync(CancellationToken cancellationToken)
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
            .Select(code => new LabelValue(StageCodes.DisplayNameOf(code), counts.TryGetValue(code, out var value) ? value : 0))
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

    private sealed record StageProjectSnapshot(int ProjectId, IReadOnlyList<StageState> Stages);

    private sealed record StageState(string StageCode, StageStatus Status, int SortOrder, DateOnly? CompletedOn);
    // END SECTION
}
