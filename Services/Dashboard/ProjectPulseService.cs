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
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Dashboard;

public sealed class ProjectPulseService
{
    // SECTION: Constants & fields
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private const string CacheKey = "Dashboard:ProjectPulse";
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public ProjectPulseService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    // SECTION: Entry point
    public Task<ProjectPulseVm> GetAsync(CancellationToken cancellationToken = default)
    {
        return _cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return BuildAsync(cancellationToken);
        })!;
    }

    // SECTION: Core aggregation
    private async Task<ProjectPulseVm> BuildAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var weekStart = GetWeekStart(today);
        var weekWindows = BuildWeekWindows(weekStart);

        var projects = await LoadProjectSnapshotsAsync(cancellationToken);
        var total = projects.Count;
        var completed = projects.Count(p => p.Status == ProjectLifecycleStatus.Completed);
        var ongoing = projects.Count(p => p.Status != ProjectLifecycleStatus.Completed && !p.IsArchived);
        var weeklyBuckets = BuildStatusBuckets(projects, weekWindows);
        var completedSeries = BuildCompletedSeries(projects, weekWindows);
        var activeSeries = BuildActiveSeries(projects, weekWindows);

        var ongoingIds = projects
            .Where(p => p.Status != ProjectLifecycleStatus.Completed && !p.IsArchived)
            .Select(p => p.Id)
            .ToArray();

        var overdue = await CountOverdueProjectsAsync(ongoingIds, today, cancellationToken);

        return new ProjectPulseVm
        {
            Total = total,
            Completed = completed,
            Ongoing = ongoing,
            All = new AllProjectsCard(total, weeklyBuckets, RepositoryLink),
            Done = new CompletedCard(completed, completedSeries, CompletedLink),
            Doing = new OngoingCard(ongoing, overdue, activeSeries, OngoingLink),
            Analytics = new AnalyticsCard(AnalyticsLink)
        };
    }

    // SECTION: Data loaders
    private async Task<List<ProjectSnapshot>> LoadProjectSnapshotsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new
            {
                p.Id,
                p.CreatedAt,
                p.CompletedOn,
                p.LifecycleStatus,
                p.IsArchived
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(p => new ProjectSnapshot(
                p.Id,
                DateOnly.FromDateTime(p.CreatedAt),
                p.CompletedOn,
                p.LifecycleStatus,
                p.IsArchived))
            .ToList();
    }

    // SECTION: Weekly series helpers
    private static IReadOnlyList<StatusBucket> BuildStatusBuckets(
        IReadOnlyList<ProjectSnapshot> projects,
        IReadOnlyList<WeekWindow> weeks)
    {
        var buckets = new List<StatusBucket>(weeks.Count);
        foreach (var week in weeks)
        {
            var endExclusive = week.EndExclusive;
            var totalToWeek = projects.Count(p => p.CreatedOn < endExclusive);
            var completedToWeek = projects.Count(p => p.CompletedOn.HasValue && p.CompletedOn.Value < endExclusive);
            var ongoingToWeek = projects.Count(p =>
                p.CreatedOn < endExclusive &&
                (p.CompletedOn == null || p.CompletedOn.Value >= endExclusive) &&
                !p.IsArchived);
            buckets.Add(new StatusBucket(completedToWeek, ongoingToWeek));
        }

        return buckets;
    }

    private static IReadOnlyList<int> BuildCompletedSeries(
        IReadOnlyList<ProjectSnapshot> projects,
        IReadOnlyList<WeekWindow> weeks)
    {
        var series = new List<int>(weeks.Count);
        foreach (var week in weeks)
        {
            var count = projects.Count(p =>
                p.CompletedOn.HasValue &&
                p.CompletedOn.Value >= week.Start &&
                p.CompletedOn.Value < week.EndExclusive);
            series.Add(count);
        }

        return series;
    }

    private static IReadOnlyList<int> BuildActiveSeries(
        IReadOnlyList<ProjectSnapshot> projects,
        IReadOnlyList<WeekWindow> weeks)
    {
        var series = new List<int>(weeks.Count);
        foreach (var week in weeks)
        {
            var endExclusive = week.EndExclusive;
            var active = projects.Count(p =>
                p.CreatedOn < endExclusive &&
                (p.CompletedOn == null || p.CompletedOn.Value >= endExclusive) &&
                !p.IsArchived);
            series.Add(active);
        }

        return series;
    }

    private static IReadOnlyList<WeekWindow> BuildWeekWindows(DateOnly currentWeekStart)
    {
        var weeks = new List<WeekWindow>(8);
        for (var i = 7; i >= 0; i--)
        {
            var start = currentWeekStart.AddDays(-7 * i);
            var endExclusive = start.AddDays(7);
            weeks.Add(new WeekWindow(start, endExclusive));
        }

        return weeks;
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var offset = dayOfWeek == 0 ? 6 : dayOfWeek - 1; // ISO weeks (Monday start)
        return date.AddDays(-offset);
    }

    // SECTION: Overdue calculation
    private async Task<int> CountOverdueProjectsAsync(
        IReadOnlyList<int> projectIds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return 0;
        }

        var stagesTask = _db.ProjectStages
            .AsNoTracking()
            .Where(s => projectIds.Contains(s.ProjectId))
            .Select(s => new StageRow(s.ProjectId, s.StageCode, s.Status, s.SortOrder, s.ActualStart, s.CompletedOn))
            .ToListAsync(cancellationToken);

        Task<List<StageDurationRow>> durationsTask;
        if (projectIds.Count > 0)
        {
            durationsTask = _db.ProjectPlanDurations
                .AsNoTracking()
                .Where(d => projectIds.Contains(d.ProjectId))
                .Select(d => new StageDurationRow(d.ProjectId, d.StageCode, d.DurationDays))
                .ToListAsync(cancellationToken);
        }
        else
        {
            durationsTask = Task.FromResult(new List<StageDurationRow>());
        }

        await Task.WhenAll(stagesTask, durationsTask);

        var stageLookup = stagesTask.Result
            .GroupBy(s => s.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.StageCode, x => x, StringComparer.OrdinalIgnoreCase));

        var durationLookup = durationsTask.Result
            .GroupBy(d => d.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.StageCode, x => x.DurationDays, StringComparer.OrdinalIgnoreCase));

        var overdueCount = 0;
        foreach (var projectId in projectIds)
        {
            var stageRows = stageLookup.TryGetValue(projectId, out var rows)
                ? rows
                : new Dictionary<string, StageRow>(StringComparer.OrdinalIgnoreCase);

            var snapshots = BuildStageSnapshots(stageRows);
            var present = PresentStageHelper.ComputePresentStageAndAge(snapshots, today);
            var stageCode = present.CurrentStageCode ?? StageCodes.FS;

            if (present.CurrentStageStartDate is { } startedOn)
            {
                var ageDays = Math.Max(0, today.DayNumber - startedOn.DayNumber);
                var duration = TryGetStageSlaDays(projectId, stageCode, durationLookup);
                if (duration.HasValue && duration.Value > 0 && ageDays > duration.Value)
                {
                    overdueCount++;
                }
            }
        }

        return overdueCount;
    }

    private static IReadOnlyList<ProjectStageStatusSnapshot> BuildStageSnapshots(IReadOnlyDictionary<string, StageRow> stageRows)
    {
        var snapshots = new List<ProjectStageStatusSnapshot>(StageCodes.All.Length);
        for (var i = 0; i < StageCodes.All.Length; i++)
        {
            var code = StageCodes.All[i];
            stageRows.TryGetValue(code, out var stageRow);
            snapshots.Add(new ProjectStageStatusSnapshot(
                code,
                stageRow?.Status ?? StageStatus.NotStarted,
                i,
                stageRow?.ActualStart,
                stageRow?.CompletedOn));
        }

        return snapshots;
    }

    private static int? TryGetStageSlaDays(
        int projectId,
        string stageCode,
        IReadOnlyDictionary<int, Dictionary<string, int?>> durationLookup)
    {
        if (!durationLookup.TryGetValue(projectId, out var stageDurations))
        {
            return null;
        }

        return stageDurations.TryGetValue(stageCode, out var days) ? days : null;
    }

    // SECTION: Links
    private const string CompletedLink = "/Projects/Completed";
    private const string OngoingLink = "/Projects/Ongoing";
    private const string RepositoryLink = "/Projects/Repository";
    private const string AnalyticsLink = "/ProjectOfficeReports/Analytics";

    // SECTION: DTOs
    private sealed record ProjectSnapshot(
        int Id,
        DateOnly CreatedOn,
        DateOnly? CompletedOn,
        ProjectLifecycleStatus Status,
        bool IsArchived);

    private sealed record WeekWindow(DateOnly Start, DateOnly EndExclusive);

    private sealed record StageRow(
        int ProjectId,
        string StageCode,
        StageStatus Status,
        int SortOrder,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    private sealed record StageDurationRow(int ProjectId, string StageCode, int? DurationDays);
}
