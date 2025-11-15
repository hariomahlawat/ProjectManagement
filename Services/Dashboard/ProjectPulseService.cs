using System;
using System.Collections.Generic;
using System.Globalization;
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
    private const string StageOtherLabel = "Other";
    private const string CategoryOtherLabel = "Other";
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

        var totalProjects = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .CountAsync(cancellationToken);

        var completedQuery = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

        var completedCount = await completedQuery.CountAsync(cancellationToken);

        var completedMonths = await LoadCompletedSeriesAsync(completedQuery, today, cancellationToken);

        var ongoingProjects = await LoadOngoingProjectsAsync(cancellationToken);

        var repositoryBreakdown = await LoadRepositoryBreakdownAsync(cancellationToken);
        var ongoingCount = ongoingProjects.Count;

        var summary = new SummaryBlock(
            Total: totalProjects,
            Completed: completedCount,
            Ongoing: ongoingCount,
            Idle: Math.Max(0, totalProjects - (completedCount + ongoingCount)));

        var completed = new CompletedBlock(
            TotalCompleted: completedCount,
            CompletionsByMonth: completedMonths,
            Link: CompletedLink);

        var ongoingBlock = await BuildOngoingBlockAsync(ongoingProjects, today, cancellationToken);

        var repository = new RepositoryBlock(
            CategoryBreakdown: repositoryBreakdown,
            Link: RepositoryLink);

        var analytics = new AnalyticsBlock(AnalyticsLink);

        return new ProjectPulseVm(summary, completed, ongoingBlock, repository, analytics);
    }

    // SECTION: Completed helpers
    private static async Task<IReadOnlyList<Point>> LoadCompletedSeriesAsync(IQueryable<Project> completedQuery, DateOnly today, CancellationToken cancellationToken)
    {
        var startMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var completions = await completedQuery
            .Where(p => p.CompletedOn != null && p.CompletedOn >= startMonth)
            .Select(p => p.CompletedOn)
            .ToListAsync(cancellationToken);

        var perMonth = completions
            .Where(d => d.HasValue)
            .GroupBy(d => new { d.Value.Year, d.Value.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.Count());

        var points = new List<Point>(12);
        var format = CultureInfo.InvariantCulture.DateTimeFormat;
        for (var i = 0; i < 12; i++)
        {
            var month = startMonth.AddMonths(i);
            var key = (month.Year, month.Month);
            perMonth.TryGetValue(key, out var count);
            var label = format.GetAbbreviatedMonthName(month.Month);
            points.Add(new Point(label, count));
        }

        return points;
    }

    // SECTION: Ongoing aggregation
    private async Task<IReadOnlyList<OngoingProjectSnapshot>> LoadOngoingProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus != ProjectLifecycleStatus.Completed)
            .Select(p => new OngoingProjectSnapshot(p.Id, p.Name))
            .ToListAsync(cancellationToken);

        return projects;
    }

    private async Task<OngoingBlock> BuildOngoingBlockAsync(IReadOnlyList<OngoingProjectSnapshot> ongoingProjects, DateOnly today, CancellationToken cancellationToken)
    {
        if (ongoingProjects.Count == 0)
        {
            return new OngoingBlock(0, 0, Array.Empty<Kv>(), OngoingLink);
        }

        var projectIds = ongoingProjects.Select(p => p.Id).ToArray();

        List<StageRow> stages;
        if (projectIds.Length > 0)
        {
            stages = await _db.ProjectStages
                .AsNoTracking()
                .Where(s => projectIds.Contains(s.ProjectId))
                .Select(s => new StageRow(s.ProjectId, s.StageCode, s.Status, s.SortOrder, s.ActualStart, s.CompletedOn))
                .ToListAsync(cancellationToken);
        }
        else
        {
            stages = new List<StageRow>();
        }

        List<StageDurationRow> durations;
        if (projectIds.Length > 0)
        {
            durations = await _db.ProjectPlanDurations
                .AsNoTracking()
                .Where(d => projectIds.Contains(d.ProjectId))
                .Select(d => new StageDurationRow(d.ProjectId, d.StageCode, d.DurationDays))
                .ToListAsync(cancellationToken);
        }
        else
        {
            durations = new List<StageDurationRow>();
        }

        var stageLookup = stages
            .GroupBy(s => s.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.StageCode, x => x, StringComparer.OrdinalIgnoreCase));

        var durationLookup = durations
            .GroupBy(d => d.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.StageCode, x => x.DurationDays, StringComparer.OrdinalIgnoreCase));

        var stageCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var overdueCount = 0;

        foreach (var project in ongoingProjects)
        {
            var stageRows = stageLookup.TryGetValue(project.Id, out var rows)
                ? rows
                : new Dictionary<string, StageRow>(StringComparer.OrdinalIgnoreCase);

            var snapshots = BuildStageSnapshots(stageRows);
            var present = PresentStageHelper.ComputePresentStageAndAge(snapshots, today);
            var stageCode = present.CurrentStageCode ?? StageCodes.FS;
            var stageLabel = StageCodes.DisplayNameOf(stageCode);
            stageCounter.TryGetValue(stageLabel, out var currentCount);
            stageCounter[stageLabel] = currentCount + 1;

            if (present.CurrentStageStartDate is { } startedOn)
            {
                var ageDays = Math.Max(0, today.DayNumber - startedOn.DayNumber);
                var duration = TryGetStageSlaDays(project.Id, stageCode, durationLookup);
                if (duration.HasValue && duration.Value > 0 && ageDays > duration.Value)
                {
                    overdueCount++;
                }
            }
        }

        var distribution = BuildSeries(stageCounter, 4, StageOtherLabel);

        return new OngoingBlock(ongoingProjects.Count, overdueCount, distribution, OngoingLink);
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

    private static int? TryGetStageSlaDays(int projectId, string stageCode, IReadOnlyDictionary<int, Dictionary<string, int?>> durationLookup)
    {
        if (!durationLookup.TryGetValue(projectId, out var stageDurations))
        {
            return null;
        }

        return stageDurations.TryGetValue(stageCode, out var days) ? days : null;
    }

    // SECTION: Repository aggregation
    private async Task<IReadOnlyList<Kv>> LoadRepositoryBreakdownAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .GroupBy(p => new
            {
                p.TechnicalCategoryId,
                Label = p.TechnicalCategory != null ? p.TechnicalCategory.Name : "Uncategorised"
            })
            .Select(g => new
            {
                g.Key.Label,
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var ordered = rows
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(r => r.Label, r => r.Count, StringComparer.OrdinalIgnoreCase);

        return BuildSeries(ordered, 6, CategoryOtherLabel);
    }

    // SECTION: Helpers
    private static IReadOnlyList<Kv> BuildSeries(IEnumerable<KeyValuePair<string, int>> items, int limit, string otherLabel)
    {
        var ordered = items
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            return Array.Empty<Kv>();
        }

        if (ordered.Count <= limit)
        {
            return ordered.Select(kv => new Kv(kv.Key, kv.Value)).ToList();
        }

        var top = ordered.Take(limit).Select(kv => new Kv(kv.Key, kv.Value)).ToList();
        var other = ordered.Skip(limit).Sum(kv => kv.Value);
        if (other > 0)
        {
            top.Add(new Kv(otherLabel, other));
        }

        return top;
    }

    // SECTION: Links
    private const string CompletedLink = "/Projects/Completed";
    private const string OngoingLink = "/Projects/Ongoing";
    private const string RepositoryLink = "/Projects/Repository";
    private const string AnalyticsLink = "/ProjectOfficeReports/Analytics";

    // SECTION: DTOs
    private sealed record OngoingProjectSnapshot(int Id, string Name);
    private sealed record StageRow(int ProjectId, string StageCode, StageStatus Status, int SortOrder, DateOnly? ActualStart, DateOnly? CompletedOn);
    private sealed record StageDurationRow(int ProjectId, string StageCode, int? DurationDays);
}
