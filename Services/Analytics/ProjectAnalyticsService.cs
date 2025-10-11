using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Analytics;

public sealed class ProjectAnalyticsService
{
    private static readonly string[] StageOrder = StageCodes.All;

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ProjectCategoryHierarchyService _categoryHierarchy;

    public ProjectAnalyticsService(ApplicationDbContext db, IClock clock, ProjectCategoryHierarchyService categoryHierarchy)
    {
        _db = db;
        _clock = clock;
        _categoryHierarchy = categoryHierarchy;
    }

    public async Task<CategoryShareResult> GetCategoryShareAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId = null,
        int? technicalCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived);

        query = ApplyLifecycleFilter(query, lifecycle);

        if (categoryId.HasValue)
        {
            var descendantCategoryIds = await _categoryHierarchy.GetCategoryAndDescendantIdsAsync(categoryId.Value, cancellationToken);
            query = query.Where(p => p.CategoryId.HasValue && descendantCategoryIds.Contains(p.CategoryId.Value));
        }

        if (technicalCategoryId.HasValue)
        {
            query = query.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
        }

        var grouped = await query
            .GroupBy(p => p.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
        {
            return new CategoryShareResult(Array.Empty<CategoryShareSlice>(), 0);
        }

        var categoryIds = grouped
            .Where(g => g.CategoryId.HasValue)
            .Select(g => g.CategoryId!.Value)
            .Distinct()
            .ToList();

        var categoryNames = await _db.ProjectCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        string? technicalCategoryName = null;
        if (technicalCategoryId.HasValue)
        {
            technicalCategoryName = await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => c.Id == technicalCategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var slices = grouped
            .Select(g => new CategoryShareSlice(
                g.CategoryId,
                g.CategoryId.HasValue && categoryNames.TryGetValue(g.CategoryId.Value, out var name)
                    ? name
                    : "Unassigned",
                g.Count,
                technicalCategoryId,
                technicalCategoryName))
            .ToList();

        var total = slices.Sum(s => s.Count);
        return new CategoryShareResult(slices, total);
    }

    public async Task<StageDistributionResult> GetStageDistributionAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived);

        query = ApplyLifecycleFilter(query, lifecycle);

        if (categoryId.HasValue)
        {
            var categoryIds = await _categoryHierarchy.GetCategoryAndDescendantIdsAsync(categoryId.Value, cancellationToken);
            query = query.Where(p => p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value));
        }

        if (technicalCategoryId.HasValue)
        {
            query = query.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
        }

        var projects = await query
            .Include(p => p.ProjectStages)
            .Select(p => new ProjectStageSnapshot(
                p.Id,
                p.LifecycleStatus,
                p.ProjectStages
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.StageCode)
                    .Select(s => new StageSnapshot(
                        s.StageCode,
                        s.Status,
                        s.SortOrder,
                        s.PlannedDue,
                        s.CompletedOn))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var stage = DetermineCurrentStage(project);
            if (stage is null || string.IsNullOrWhiteSpace(stage.StageCode))
            {
                continue;
            }

            counts.TryGetValue(stage.StageCode, out var existing);
            counts[stage.StageCode] = existing + 1;
        }

        var ordered = StageOrder
            .Where(code => counts.ContainsKey(code))
            .Concat(counts.Keys.Where(code => !StageOrder.Contains(code, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new StageDistributionItem(
                code,
                StageCodes.DisplayNameOf(code),
                counts.TryGetValue(code, out var value) ? value : 0))
            .ToList();

        return new StageDistributionResult(ordered, lifecycle);
    }

    public async Task<LifecycleBreakdownResult> GetLifecycleBreakdownAsync(
        int? categoryId,
        int? technicalCategoryId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived);

        if (categoryId.HasValue)
        {
            var categoryIds = await _categoryHierarchy.GetCategoryAndDescendantIdsAsync(categoryId.Value, cancellationToken);
            query = query.Where(p => p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value));
        }

        if (technicalCategoryId.HasValue)
        {
            query = query.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
        }

        var raw = await query
            .GroupBy(p => p.LifecycleStatus)
            .Select(g => new LifecycleBreakdownItem(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var result = Enum.GetValues<ProjectLifecycleStatus>()
            .Select(status =>
                raw.FirstOrDefault(r => r.Status == status) ?? new LifecycleBreakdownItem(status, 0))
            .ToList();

        return new LifecycleBreakdownResult(result);
    }

    public async Task<MonthlyStageCompletionsResult> GetMonthlyStageCompletionsAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        DateOnly fromMonth,
        DateOnly toMonth,
        CancellationToken cancellationToken = default)
    {
        if (toMonth < fromMonth)
        {
            (fromMonth, toMonth) = (toMonth, fromMonth);
        }

        var months = new List<DateOnly>();
        var cursor = new DateOnly(fromMonth.Year, fromMonth.Month, 1);
        var limit = new DateOnly(toMonth.Year, toMonth.Month, 1);
        while (cursor <= limit)
        {
            months.Add(cursor);
            cursor = cursor.AddMonths(1);
        }

        var nextLimitExclusive = limit.AddMonths(1);

        var query = _db.ProjectStages
            .AsNoTracking()
            .Where(s => s.CompletedOn != null)
            .Where(s => s.Project != null && !s.Project.IsDeleted && !s.Project.IsArchived)
            .Where(s => s.CompletedOn!.Value >= fromMonth && s.CompletedOn!.Value < nextLimitExclusive);

        if (categoryId.HasValue)
        {
            var categoryIds = await _categoryHierarchy.GetCategoryAndDescendantIdsAsync(categoryId.Value, cancellationToken);
            query = query.Where(s => s.Project!.CategoryId.HasValue && categoryIds.Contains(s.Project!.CategoryId.Value));
        }

        if (technicalCategoryId.HasValue)
        {
            query = query.Where(s => s.Project!.TechnicalCategoryId == technicalCategoryId.Value);
        }

        query = ApplyLifecycleFilter(query, lifecycle);

        var rows = await query
            .Select(s => new StageCompletionRow(
                s.StageCode,
                s.CompletedOn!.Value,
                s.ProjectId))
            .ToListAsync(cancellationToken);

        var monthIndex = months
            .Select((m, idx) => (m, idx))
            .ToDictionary(x => x.m, x => x.idx);

        var seriesMap = StageOrder
            .ToDictionary(code => code, _ => new int[months.Count], StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = new DateOnly(row.CompletedOn.Year, row.CompletedOn.Month, 1);
            if (!monthIndex.TryGetValue(key, out var idx))
            {
                continue;
            }

            if (!seriesMap.TryGetValue(row.StageCode, out var arr))
            {
                arr = new int[months.Count];
                seriesMap[row.StageCode] = arr;
            }

            arr[idx] += 1;
        }

        var series = seriesMap
            .Where(pair => pair.Value.Any(v => v > 0))
            .OrderBy(pair => Array.IndexOf(StageOrder, pair.Key) is var order && order >= 0 ? order : int.MaxValue)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new MonthlyStageSeries(
                pair.Key,
                StageCodes.DisplayNameOf(pair.Key),
                Array.AsReadOnly(pair.Value)))
            .ToList();

        var nowIst = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst());
        var thisMonth = new DateOnly(nowIst.Year, nowIst.Month, 1);
        var nextMonth = thisMonth.AddMonths(1);

        var filteredThisMonth = rows
            .Where(r => r.CompletedOn >= thisMonth && r.CompletedOn < nextMonth)
            .ToList();

        var totalThisMonth = filteredThisMonth.Count;
        var projectsAdvancing = filteredThisMonth
            .GroupBy(r => r.ProjectId)
            .Count(g => g.Count() >= 2);

        var topStage = filteredThisMonth
            .GroupBy(r => r.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Stage, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var monthsInfo = months
            .Select(m => new MonthlyStageBucket(
                m.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                m.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                m))
            .ToList();

        var kpis = new StageCompletionKpis(
            totalThisMonth,
            projectsAdvancing,
            topStage?.Stage,
            topStage?.Stage is string code ? StageCodes.DisplayNameOf(code) : null,
            topStage?.Count ?? 0);

        return new MonthlyStageCompletionsResult(monthsInfo, series, kpis);
    }

    public async Task<SlipBucketResult> GetSlipBucketsAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        CancellationToken cancellationToken = default)
    {
        var projects = await LoadProjectsForHealthAsync(lifecycle, categoryId, technicalCategoryId, null, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));

        var buckets = SlipBucketDefinitions.CreateBuckets();

        foreach (var project in projects)
        {
            var slipByStage = StageHealthCalculator.Compute(project.ToStages(), today).SlipByStage;
            var maxSlip = slipByStage.Count == 0 ? 0 : slipByStage.Values.Max();
            var bucket = buckets.FirstOrDefault(b => b.Contains(maxSlip));
            (bucket ?? buckets[^1]).Count++;
        }

        return new SlipBucketResult(buckets.Select(b => new SlipBucketItem(b.Key, b.Label, b.Count)).ToList());
    }

    public async Task<IReadOnlyCollection<int>> GetProjectIdsForSlipBucketAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        string bucketKey,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? expandedCategoryIds = null)
    {
        if (string.IsNullOrWhiteSpace(bucketKey))
        {
            return Array.Empty<int>();
        }

        var projects = await LoadProjectsForHealthAsync(
            lifecycle,
            categoryId,
            technicalCategoryId,
            expandedCategoryIds,
            cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));

        var bucket = SlipBucketDefinitions.CreateBuckets()
            .FirstOrDefault(b => string.Equals(b.Key, bucketKey, StringComparison.OrdinalIgnoreCase));

        if (bucket is null)
        {
            return Array.Empty<int>();
        }

        var matches = new HashSet<int>();
        foreach (var project in projects)
        {
            var slipByStage = StageHealthCalculator.Compute(project.ToStages(), today).SlipByStage;
            var maxSlip = slipByStage.Count == 0 ? 0 : slipByStage.Values.Max();
            if (bucket.Contains(maxSlip))
            {
                matches.Add(project.Id);
            }
        }

        return matches;
    }

    public async Task<TopOverdueProjectsResult> GetTopOverdueProjectsAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            take = 5;
        }

        var projects = await LoadProjectsForHealthAsync(lifecycle, categoryId, technicalCategoryId, null, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));

        var ranked = new List<TopOverdueProject>();

        foreach (var project in projects)
        {
            var health = StageHealthCalculator.Compute(project.ToStages(), today);
            if (health.SlipByStage.Count == 0)
            {
                continue;
            }

            var worst = health.SlipByStage
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .First();

            var slipDays = worst.Value;
            if (slipDays <= 0)
            {
                continue;
            }

            ranked.Add(new TopOverdueProject(
                project.Id,
                project.Name,
                project.CategoryName ?? "Unassigned",
                project.TechnicalCategoryName,
                worst.Key,
                StageCodes.DisplayNameOf(worst.Key),
                slipDays));
        }

        var ordered = ranked
            .OrderByDescending(r => r.SlipDays)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

        return new TopOverdueProjectsResult(ordered);
    }

    private async Task<List<ProjectHealthSnapshot>> LoadProjectsForHealthAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        IReadOnlyCollection<int>? resolvedCategoryIds,
        CancellationToken cancellationToken)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived);

        query = ApplyLifecycleFilter(query, lifecycle);

        if (resolvedCategoryIds is { Count: > 0 })
        {
            query = query.Where(p => p.CategoryId.HasValue && resolvedCategoryIds.Contains(p.CategoryId.Value));
        }
        else if (categoryId.HasValue)
        {
            var categoryIds = await _categoryHierarchy.GetCategoryAndDescendantIdsAsync(categoryId.Value, cancellationToken);
            query = query.Where(p => p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value));
        }

        if (technicalCategoryId.HasValue)
        {
            query = query.Where(p => p.TechnicalCategoryId == technicalCategoryId);
        }

        var projects = await query
            .Include(p => p.ProjectStages)
            .Select(p => new ProjectHealthSnapshot(
                p.Id,
                p.Name,
                p.LifecycleStatus,
                p.Category != null ? p.Category.Name : null,
                p.TechnicalCategory != null ? p.TechnicalCategory.Name : null,
                p.ProjectStages
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.StageCode)
                    .Select(s => new StageSnapshot(
                        s.StageCode,
                        s.Status,
                        s.SortOrder,
                        s.PlannedDue,
                        s.CompletedOn))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return projects;
    }

    private static IQueryable<Project> ApplyLifecycleFilter(
        IQueryable<Project> query,
        ProjectLifecycleFilter lifecycle)
    {
        return lifecycle switch
        {
            ProjectLifecycleFilter.Active => query.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active),
            ProjectLifecycleFilter.Completed => query.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed),
            ProjectLifecycleFilter.Cancelled => query.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Cancelled),
            ProjectLifecycleFilter.Legacy => query.Where(p => p.IsLegacy),
            _ => query.Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active
                || p.LifecycleStatus == ProjectLifecycleStatus.Completed
                || p.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        };
    }

    private static IQueryable<ProjectStage> ApplyLifecycleFilter(
        IQueryable<ProjectStage> query,
        ProjectLifecycleFilter lifecycle)
    {
        return lifecycle switch
        {
            ProjectLifecycleFilter.Active => query.Where(s => s.Project!.LifecycleStatus == ProjectLifecycleStatus.Active),
            ProjectLifecycleFilter.Completed => query.Where(s => s.Project!.LifecycleStatus == ProjectLifecycleStatus.Completed),
            ProjectLifecycleFilter.Cancelled => query.Where(s => s.Project!.LifecycleStatus == ProjectLifecycleStatus.Cancelled),
            ProjectLifecycleFilter.Legacy => query.Where(s => s.Project!.IsLegacy),
            _ => query.Where(s => s.Project!.LifecycleStatus == ProjectLifecycleStatus.Active
                || s.Project!.LifecycleStatus == ProjectLifecycleStatus.Completed
                || s.Project!.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        };
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

    private sealed record StageSnapshot(
        string StageCode,
        StageStatus Status,
        int SortOrder,
        DateOnly? PlannedDue,
        DateOnly? CompletedOn);

    private sealed record ProjectStageSnapshot(
        int Id,
        ProjectLifecycleStatus Status,
        IReadOnlyList<StageSnapshot> Stages);

    private sealed record ProjectHealthSnapshot(
        int Id,
        string Name,
        ProjectLifecycleStatus Status,
        string? CategoryName,
        string? TechnicalCategoryName,
        IReadOnlyList<StageSnapshot> Stages)
    {
        public IEnumerable<ProjectStage> ToStages() => Stages.Select(s => new ProjectStage
        {
            StageCode = s.StageCode,
            Status = s.Status,
            PlannedDue = s.PlannedDue,
            CompletedOn = s.CompletedOn
        });
    }

    private sealed record StageCompletionRow(
        string StageCode,
        DateOnly CompletedOn,
        int ProjectId);

    private sealed record SlipBucketDefinition(string Key, string Label, int MinInclusive, int? MaxInclusive)
    {
        public int Count { get; set; }

        public bool Contains(int value)
        {
            if (value < MinInclusive)
            {
                return false;
            }

            return MaxInclusive is null || value <= MaxInclusive.Value;
        }
    }

    private static class SlipBucketDefinitions
    {
        public static List<SlipBucketDefinition> CreateBuckets() => new()
        {
            new SlipBucketDefinition("0", "0 days", 0, 0),
            new SlipBucketDefinition("1-7", "1-7 days", 1, 7),
            new SlipBucketDefinition("8-30", "8-30 days", 8, 30),
            new SlipBucketDefinition("31+", "31+ days", 31, null)
        };
    }
}

public sealed record CategoryShareResult(IReadOnlyList<CategoryShareSlice> Slices, int Total);

public sealed record CategoryShareSlice(
    int? CategoryId,
    string CategoryName,
    int Count,
    int? TechnicalCategoryId,
    string? TechnicalCategoryName);

public sealed record StageDistributionResult(IReadOnlyList<StageDistributionItem> Items, ProjectLifecycleFilter Lifecycle);

public sealed record StageDistributionItem(string StageCode, string StageName, int Count);

public sealed record LifecycleBreakdownResult(IReadOnlyList<LifecycleBreakdownItem> Items);

public sealed record LifecycleBreakdownItem(ProjectLifecycleStatus Status, int Count);

public sealed record MonthlyStageCompletionsResult(
    IReadOnlyList<MonthlyStageBucket> Months,
    IReadOnlyList<MonthlyStageSeries> Series,
    StageCompletionKpis Kpis);

public sealed record MonthlyStageBucket(string Key, string Label, DateOnly Start);

public sealed record MonthlyStageSeries(string StageCode, string StageName, IReadOnlyList<int> Counts);

public sealed record StageCompletionKpis(
    int TotalCompletionsThisMonth,
    int ProjectsAdvancedTwoOrMoreStages,
    string? TopStageCode,
    string? TopStageName,
    int TopStageCount);

public sealed record SlipBucketResult(IReadOnlyList<SlipBucketItem> Buckets);

public sealed record SlipBucketItem(string Key, string Label, int Count);

public sealed record TopOverdueProjectsResult(IReadOnlyList<TopOverdueProject> Projects);

public sealed record TopOverdueProject(
    int ProjectId,
    string Name,
    string Category,
    string? TechnicalCategory,
    string StageCode,
    string StageName,
    int SlipDays);
