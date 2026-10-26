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
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Analytics;

public sealed class ProjectAnalyticsService : IProjectAnalyticsService
{
    private static readonly string[] StageOrder = StageCodes.All;
    private static readonly string[] StageTimeBuckets =
    {
        StageTimeBucketKeys.UpToTwoCrore,
        StageTimeBucketKeys.AboveTwoCrore
    };

    /// <summary>The cost-band boundary; a project costing exactly this much is in the lower band.</summary>
    private const decimal TwoCroreRupees = 20_000_000m;
    private const string UncategorizedRootName = "Uncategorized";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ProjectCategoryHierarchyService _categoryHierarchy;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
    private readonly IAuthoritativeIpaPositionResolver _ipaResolver;
    private readonly IStageStartInferenceService _stageStarts;

    public ProjectAnalyticsService(
        ApplicationDbContext db,
        IClock clock,
        ProjectCategoryHierarchyService categoryHierarchy,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider)
        : this(
            db,
            clock,
            categoryHierarchy,
            workflowStageMetadataProvider,
            new AuthoritativeIpaPositionResolver(db))
    {
    }

    public ProjectAnalyticsService(
        ApplicationDbContext db,
        IClock clock,
        ProjectCategoryHierarchyService categoryHierarchy,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IAuthoritativeIpaPositionResolver ipaResolver)
        : this(
            db,
            clock,
            categoryHierarchy,
            workflowStageMetadataProvider,
            ipaResolver,
            new StageStartInferenceService(
                new ProjectStageWorkflowPolicy(db, workflowStageMetadataProvider),
                new ArppIpaStageAuthorityService(db)))
    {
    }

    public ProjectAnalyticsService(
        ApplicationDbContext db,
        IClock clock,
        ProjectCategoryHierarchyService categoryHierarchy,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IAuthoritativeIpaPositionResolver ipaResolver,
        IStageStartInferenceService stageStarts)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _categoryHierarchy = categoryHierarchy ?? throw new ArgumentNullException(nameof(categoryHierarchy));
        _workflowStageMetadataProvider = workflowStageMetadataProvider ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
        _ipaResolver = ipaResolver ?? throw new ArgumentNullException(nameof(ipaResolver));
        _stageStarts = stageStarts ?? throw new ArgumentNullException(nameof(stageStarts));
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
                _workflowStageMetadataProvider.GetDisplayName(null, code),
                counts.TryGetValue(code, out var value) ? value : 0))
            .ToList();

        return new StageDistributionResult(ordered, lifecycle);
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
                _workflowStageMetadataProvider.GetDisplayName(null, worst.Key),
                slipDays));
        }

        var ordered = ranked
            .OrderByDescending(r => r.SlipDays)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

        return new TopOverdueProjectsResult(ordered);
    }

    public async Task<StageTimeInsightsVm> GetStageTimeInsightsAsync(
        int? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var stageQuery = _db.ProjectStages
            .AsNoTracking()
            .Where(s => s.Project != null
                && !s.Project.IsDeleted
                && !s.Project.IsArchived
                && (s.Project.LifecycleStatus == ProjectLifecycleStatus.Active
                    || s.Project.LifecycleStatus == ProjectLifecycleStatus.Completed));

        if (categoryId.HasValue)
        {
            // The filter means the category and everything under it, as every other filter on this service does.
            var categoryIds = await _categoryHierarchy.GetCategoryAndDescendantIdsAsync(categoryId.Value, cancellationToken);
            stageQuery = stageQuery.Where(s => s.Project != null && s.Project.CategoryId.HasValue && categoryIds.Contains(s.Project.CategoryId.Value));
        }

        // Every stage of the eligible projects is read, not only the finished ones: a stage
        // with no recorded start is measured from the day after its predecessors finished.
        var allStages = await stageQuery
            .Select(s => new StageTimeStageRow(
                s.ProjectId,
                s.StageCode,
                s.Status,
                s.SortOrder,
                s.ActualStart,
                s.CompletedOn,
                s.Project!.LifecycleStatus == ProjectLifecycleStatus.Completed,
                s.Project!.CategoryId))
            .ToListAsync(cancellationToken);

        var starts = await _stageStarts.ResolveAsync(
            allStages
                .GroupBy(row => row.ProjectId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<StageStartFact>)group
                        .Select(row => new StageStartFact(row.StageCode, row.Status, row.ActualStart, row.CompletedOn))
                        .ToList()),
            cancellationToken);

        var stageRows = allStages
            .Where(row => row.CompletedOn.HasValue && !string.Equals(row.StageCode, StageCodes.TOT))
            .Select(row => (Row: row, Start: starts.TryGetValue(row.ProjectId, out var projectStarts)
                && projectStarts.TryGetValue(row.StageCode ?? string.Empty, out var start)
                    ? start
                    : (EffectiveStageStart?)null))
            .Where(item => item.Start.HasValue)
            .Select(item => new StageTimeSpanRow(
                item.Row.ProjectId,
                item.Row.StageCode,
                item.Row.StageOrder,
                item.Start!.Value.Date,
                item.Row.CompletedOn!.Value,
                item.Start!.Value.IsInferred,
                item.Row.IsCompletedProject,
                item.Row.CategoryId))
            .ToList();

        if (stageRows.Count == 0)
        {
            return new StageTimeInsightsVm
            {
                SelectedCategoryId = categoryId
            };
        }

        var projectCosts = await LoadProjectCostLookupAsync(stageRows.Select(row => row.ProjectId).Distinct().ToArray(), cancellationToken);
        var categoryHierarchy = await LoadCategoryHierarchyAsync(cancellationToken);

        // Every finished stage is a span. The cost band is null for a project with no cost figure
        // yet: such a span still counts in the all-projects hotspots and the category rows, and only
        // the by-band rows skip it. The category bucket is the project's root category (every project has one, or "Uncategorized").
        var spans = stageRows
            .Select(row => new StageTimeBucketSpan(
                row.ProjectId,
                row.StageCode ?? string.Empty,
                _workflowStageMetadataProvider.GetDisplayName(null, row.StageCode ?? string.Empty),
                row.StageOrder,
                TryResolveCostBucket(projectCosts.TryGetValue(row.ProjectId, out var cost) ? cost : null),
                ProjectCategoryHierarchyResolver.ResolveRoot(row.CategoryId, categoryHierarchy)?.Name ?? UncategorizedRootName,
                CalculateStageDurationDays(row.ActualStart, row.CompletedOn),
                row.IsCompletedProject))
            .ToList();

        var rows = EnsureAllStageBuckets(BuildBucketRows(spans, span => span.CostBucket), StageTimeBuckets);
        var categoryRows = EnsureAllStageBuckets(
            BuildBucketRows(spans, span => span.CategoryBucket),
            spans.Select(span => span.CategoryBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());

        var hotspots = BuildStageHotspots(spans);

        return new StageTimeInsightsVm
        {
            Rows = rows,
            CategoryRows = categoryRows,
            StageHotspots = hotspots,
            SelectedCategoryId = categoryId,
            MeasuredStageCount = stageRows.Count,
            InferredStartStageCount = stageRows.Count(row => row.IsStartInferred)
        };
    }

    /// <summary>One row per stage and bucket; spans whose bucket is null are left out.</summary>
    private static List<StageTimeBucketRowVm> BuildBucketRows(
        IReadOnlyCollection<StageTimeBucketSpan> spans,
        Func<StageTimeBucketSpan, string?> bucketOf)
    {
        return spans
            .Select(span => (Span: span, Bucket: bucketOf(span)))
            .Where(item => item.Bucket is not null)
            // One row per stage and bucket. The same stage can carry different sort orders across
            // workflow versions, so the order is not part of the key; the earliest is kept for sorting.
            .GroupBy(item => new StageTimeBucketKey(item.Span.StageKey, item.Span.StageName, item.Bucket!), item => item.Span)
            .Select(group =>
            {
                var durations = group
                    .Select(item => item.Days)
                    .OrderBy(value => value)
                    .ToArray();
                var projectIds = group
                    .Select(item => item.ProjectId)
                    .Distinct()
                    .ToArray();
                var completedProjects = group
                    .Where(item => item.IsCompletedProject)
                    .Select(item => item.ProjectId)
                    .Distinct()
                    .Count();
                var ongoingProjects = projectIds.Length - completedProjects;

                return new StageTimeBucketRowVm
                {
                    StageKey = group.Key.StageKey,
                    StageName = group.Key.StageName,
                    StageOrder = group.Min(item => item.StageOrder),
                    Bucket = group.Key.Bucket,
                    MedianDays = AnalyticsAggregation.CalculateMedian(durations),
                    AverageDays = durations.Length == 0 ? 0 : durations.Average(),
                    ProjectCount = projectIds.Length,
                    CompletedProjectCount = completedProjects,
                    OngoingProjectCount = ongoingProjects
                };
            })
            .ToList();
    }

    private static IReadOnlyList<StageHotspotPointVm> BuildStageHotspots(IReadOnlyCollection<StageTimeBucketSpan> spans)
    {
        if (spans.Count == 0)
        {
            return Array.Empty<StageHotspotPointVm>();
        }

        var points = spans
            .GroupBy(span => new StageHotspotKey(span.StageKey, span.StageName))
            .Select(group =>
            {
                var durations = group
                    .Select(item => item.Days)
                    .Where(value => value >= 0)
                    .OrderBy(value => value)
                    .ToArray();

                if (durations.Length == 0)
                {
                    return null;
                }

                var projectCount = group
                    .Select(item => item.ProjectId)
                    .Distinct()
                    .Count();

                return new StageHotspotPointVm
                {
                    StageKey = group.Key.StageKey,
                    StageName = group.Key.StageName,
                    StageOrder = group.Min(item => item.StageOrder),
                    MedianDays = AnalyticsAggregation.CalculateMedian(durations),
                    AverageDays = durations.Average(),
                    ProjectCount = projectCount
                };
            })
            .Where(point => point is not null)
            .Select(point => point!)
            .OrderByDescending(point => point.MedianDays)
            .ThenBy(point => point.StageOrder)
            .ThenBy(point => point.StageName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return points;
    }

    /// <summary>Every workflow stage (ToT excepted) gets a row for every bucket, at zero where nothing finished.</summary>
    private List<StageTimeBucketRowVm> EnsureAllStageBuckets(List<StageTimeBucketRowVm> rows, IReadOnlyList<string> buckets)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;

        foreach (var stageCode in StageOrder)
        {
            // SECTION: Skip TOT stage from insights padding
            if (StageCodes.IsTot(stageCode))
            {
                continue;
            }
            // END SECTION

            var stageOrder = rows
                .Where(row => comparer.Equals(row.StageKey, stageCode))
                .Select(row => row.StageOrder)
                .Cast<int?>()
                .FirstOrDefault()
                ?? Array.IndexOf(StageOrder, stageCode);

            var stageName = _workflowStageMetadataProvider.GetDisplayName(null, stageCode);

            foreach (var bucket in buckets)
            {
                var exists = rows.Any(row => comparer.Equals(row.StageKey, stageCode)
                    && comparer.Equals(row.Bucket, bucket));
                if (exists)
                {
                    continue;
                }

                rows.Add(new StageTimeBucketRowVm
                {
                    StageKey = stageCode,
                    StageName = stageName,
                    StageOrder = stageOrder,
                    Bucket = bucket,
                    MedianDays = 0,
                    AverageDays = 0,
                    ProjectCount = 0,
                    CompletedProjectCount = 0,
                    OngoingProjectCount = 0
                });
            }
        }

        return rows
            .OrderBy(row => row.StageOrder)
            .ThenBy(row => row.StageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => BucketSortOrder(row.Bucket))
            .ThenBy(row => row.Bucket, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    // The project's cost (in rupees) under the one shared rule, for the up-to-2-Cr / above-2-Cr buckets.
    private async Task<Dictionary<int, decimal?>> LoadProjectCostLookupAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        var costs = await new ProjectCostResolver(_db, _ipaResolver)
            .ResolveCostInCrAsync(projectIds, cancellationToken);

        return costs
            .Where(pair => pair.Value.AmountInRupees.HasValue)
            .ToDictionary(pair => pair.Key, pair => pair.Value.AmountInRupees);
    }

    private static string? TryResolveCostBucket(decimal? projectCost)
    {
        if (!projectCost.HasValue)
        {
            return null;
        }

        return projectCost.Value <= TwoCroreRupees
            ? StageTimeBucketKeys.UpToTwoCrore
            : StageTimeBucketKeys.AboveTwoCrore;
    }

    private static double CalculateStageDurationDays(DateOnly start, DateOnly end)
        => StageDurationCalculator.InclusiveCalendarDays(start, end);

    private static int BucketSortOrder(string bucket)
    {
        if (bucket.Equals(StageTimeBucketKeys.UpToTwoCrore, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (bucket.Equals(StageTimeBucketKeys.AboveTwoCrore, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private sealed record StageTimeStageRow(
        int ProjectId,
        string StageCode,
        StageStatus Status,
        int StageOrder,
        DateOnly? ActualStart,
        DateOnly? CompletedOn,
        bool IsCompletedProject,
        int? CategoryId);

    private sealed record StageTimeSpanRow(
        int ProjectId,
        string StageCode,
        int StageOrder,
        DateOnly ActualStart,
        DateOnly CompletedOn,
        bool IsStartInferred,
        bool IsCompletedProject,
        int? CategoryId);

    private sealed record StageTimeBucketSpan(
        int ProjectId,
        string StageKey,
        string StageName,
        int StageOrder,
        string? CostBucket,
        string CategoryBucket,
        double Days,
        bool IsCompletedProject);

    private sealed record StageTimeBucketKey(string StageKey, string StageName, string Bucket);

    private sealed record StageHotspotKey(string StageKey, string StageName);
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
