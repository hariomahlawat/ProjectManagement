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
        var completedBase = baseQuery
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed && !p.IsArchived);
        var completed = await completedBase.CountAsync(cancellationToken);
        var completedUnique = await completedBase.Where(p => !p.IsBuild).CountAsync(cancellationToken);
        var completedRebuild = await completedBase.Where(p => p.IsBuild).CountAsync(cancellationToken);
        var ongoing = await baseQuery
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active && !p.IsArchived)
            .CountAsync(cancellationToken);

        var ongoingByCategory = await BuildOngoingByCategoryAsync(cancellationToken);
        var (technicalTop, remainingTechnical) = await BuildTechnicalCategorySeriesAsync(cancellationToken);
        var uniqueCompletedByTech = await BuildUniqueCompletedByTechnicalCategoryTreemapAsync(cancellationToken);
        var uniqueCompletedByType = await BuildUniqueCompletedByProjectTypeTreemapAsync(cancellationToken);
        var availableForProliferation = await CountProliferationEligibleAsync(cancellationToken);

        return new ProjectPulseVm
        {
            ProliferationEligible = availableForProliferation,
            AnalyticsUrl = AnalyticsPage,
            CompletedCount = completed,
            CompletedUniqueCount = completedUnique,
            CompletedRebuildCount = completedRebuild,
            OngoingCount = ongoing,
            TotalProjects = total,
            OngoingByProjectCategory = ongoingByCategory,
            AllByTechnicalCategoryTop = technicalTop,
            RemainingTechCategories = remainingTechnical,
            UniqueCompletedByTechnicalCategory = uniqueCompletedByTech,
            UniqueCompletedByProjectType = uniqueCompletedByType,
            CompletedUrl = CompletedPage,
            OngoingUrl = OngoingPage,
            RepositoryUrl = RepositoryPage
        };
    }
    // END SECTION

    // SECTION: Series builders
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

    private async Task<IReadOnlyList<CategorySlice>> BuildOngoingByCategoryAsync(CancellationToken cancellationToken)
    {
        // Identify the parent category for each active project
        var categorizedProjects = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Select(p => new
            {
                ParentCategoryId = p.CategoryId.HasValue
                    ? (p.Category!.ParentId ?? p.CategoryId)
                    : (int?)null
            })
            .ToListAsync(cancellationToken);

        // Group by parent category id and count projects
        var groupedByParent = categorizedProjects
            .GroupBy(x => x.ParentCategoryId)
            .Select(g => new
            {
                ParentCategoryId = g.Key,
                Count = g.Count()
            })
            .ToList();

        // Load category names for all parent ids
        var categoryIds = groupedByParent
            .Where(x => x.ParentCategoryId.HasValue)
            .Select(x => x.ParentCategoryId!.Value)
            .Distinct()
            .ToList();

        var categoryNames = await _db.ProjectCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Convert grouped data into CategorySlice entries
        var ongoingSeries = groupedByParent
            .Select(g =>
            {
                var label = "Unknown";

                if (g.ParentCategoryId.HasValue)
                {
                    if (categoryNames.TryGetValue(g.ParentCategoryId.Value, out var resolvedLabel))
                    {
                        label = resolvedLabel;
                    }
                }
                else
                {
                    label = "Uncategorized";
                }

                return new CategorySlice(label, g.Count);
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ongoingSeries;
    }

    private async Task<IReadOnlyList<TreemapNode>> BuildUniqueCompletedByTechnicalCategoryTreemapAsync(
        CancellationToken cancellationToken)
    {
        var grouped = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                && !p.IsArchived
                && p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsBuild)
            .GroupBy(p => p.TechnicalCategoryId)
            .Select(g => new { TechnicalCategoryId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
        {
            return Array.Empty<TreemapNode>();
        }

        var unclassifiedCount = grouped
            .Where(x => x.TechnicalCategoryId == null)
            .Select(x => x.Count)
            .FirstOrDefault();

        var classified = grouped
            .Where(x => x.TechnicalCategoryId != null)
            .Select(x => new TreemapCount(x.TechnicalCategoryId!.Value, x.Count))
            .ToList();

        var names = await BuildTechnicalCategoryNameMapAsync(classified, cancellationToken);
        var ordered = classified
            .Select(x => new TreemapBucket(
                x.Id,
                names.TryGetValue(x.Id, out var name) ? name : "Unknown",
                x.Count))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildTreemapSeries(ordered, unclassifiedCount);
    }

    private async Task<IReadOnlyList<TreemapNode>> BuildUniqueCompletedByProjectTypeTreemapAsync(
        CancellationToken cancellationToken)
    {
        var grouped = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                && !p.IsArchived
                && p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsBuild)
            .GroupBy(p => p.ProjectTypeId)
            .Select(g => new { ProjectTypeId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
        {
            return Array.Empty<TreemapNode>();
        }

        var unclassifiedCount = grouped
            .Where(x => x.ProjectTypeId == null)
            .Select(x => x.Count)
            .FirstOrDefault();

        var classified = grouped
            .Where(x => x.ProjectTypeId != null)
            .Select(x => new TreemapCount(x.ProjectTypeId!.Value, x.Count))
            .ToList();

        var names = await BuildProjectTypeNameMapAsync(classified, cancellationToken);
        var ordered = classified
            .Select(x => new TreemapBucket(
                x.Id,
                names.TryGetValue(x.Id, out var name) ? name : "Unknown",
                x.Count))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildTreemapSeries(ordered, unclassifiedCount);
    }
    // END SECTION

    // SECTION: Treemap helpers
    private async Task<IReadOnlyDictionary<int, string>> BuildTechnicalCategoryNameMapAsync(
        IReadOnlyCollection<TreemapCount> classified,
        CancellationToken cancellationToken)
    {
        var ids = classified.Select(x => x.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        return await _db.TechnicalCategories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, string>> BuildProjectTypeNameMapAsync(
        IReadOnlyCollection<TreemapCount> classified,
        CancellationToken cancellationToken)
    {
        var ids = classified.Select(x => x.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        return await _db.ProjectTypes
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }

    private static IReadOnlyList<TreemapNode> BuildTreemapSeries(
        IReadOnlyList<TreemapBucket> ordered,
        int unclassifiedCount)
    {
        var top = ordered.Take(5).ToList();
        var rest = ordered.Skip(5).ToList();
        var othersCount = rest.Sum(x => x.Count);
        var nodes = new List<TreemapNode>();

        foreach (var node in top)
        {
            nodes.Add(new TreemapNode(node.Label, node.Count, null));
        }

        if (unclassifiedCount > 0)
        {
            nodes.Add(new TreemapNode("Unclassified", unclassifiedCount, null));
        }

        if (othersCount > 0)
        {
            nodes.Add(new TreemapNode("Others", othersCount, null));
        }

        return nodes;
    }

    private sealed record TreemapCount(int Id, int Count);
    private sealed record TreemapBucket(int Id, string Label, int Count);
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

    // END SECTION
}
