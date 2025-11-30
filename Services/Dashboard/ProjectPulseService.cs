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
        var completed = await baseQuery
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .CountAsync(cancellationToken);
        var ongoing = await baseQuery
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active && !p.IsArchived)
            .CountAsync(cancellationToken);

        var completedByYear = await BuildCompletedByYearAsync(cancellationToken);
        var ongoingByCategory = await BuildOngoingByCategoryAsync(cancellationToken);
        var totalOngoing = ongoingByCategory.Sum(x => x.ProjectCount);
        var (technicalTop, remainingTechnical) = await BuildTechnicalCategorySeriesAsync(cancellationToken);
        var availableForProliferation = await CountProliferationEligibleAsync(cancellationToken);

        return new ProjectPulseVm
        {
            ProliferationEligible = availableForProliferation,
            AnalyticsUrl = AnalyticsPage,
            CompletedCount = completed,
            OngoingCount = ongoing,
            TotalOngoingProjects = totalOngoing,
            TotalProjects = total,
            CompletedByYear = completedByYear,
            OngoingByCategory = ongoingByCategory,
            AllByTechnicalCategoryTop = technicalTop,
            RemainingTechCategories = remainingTechnical,
            CompletedUrl = CompletedPage,
            OngoingUrl = OngoingPage,
            RepositoryUrl = RepositoryPage
        };
    }
    // END SECTION

    // SECTION: Series builders
    private async Task<IReadOnlyList<BarPoint>> BuildCompletedByYearAsync(CancellationToken cancellationToken)
    {
        var completedSeries = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                && !p.IsArchived
                && p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(p => new
            {
                Year = p.CompletedOn.HasValue
                    ? (int?)p.CompletedOn.Value.Year
                    : p.CompletedYear
            })
            .Where(p => p.Year.HasValue)
            .GroupBy(p => p.Year!.Value)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Year)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (completedSeries.Count == 0)
        {
            var fallbackCount = await _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed)
                .CountAsync(cancellationToken);

            if (fallbackCount > 0)
            {
                return new[] { new BarPoint("Unknown", fallbackCount) };
            }

            return Array.Empty<BarPoint>();
        }

        return completedSeries
            .OrderBy(x => x.Year)
            .Select(x => new BarPoint(x.Year.ToString(CultureInfo.InvariantCulture), x.Count))
            .ToList();
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

    private async Task<IReadOnlyList<OngoingCategorySlice>> BuildOngoingByCategoryAsync(CancellationToken cancellationToken)
    {
        // Identify the parent category for each active project and group counts
        var ongoingSeries = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Active)
            .GroupBy(p => p.Category != null
                ? (p.Category.Parent != null ? p.Category.Parent.Name : p.Category.Name)
                : "Uncategorised")
            .Select(g => new OngoingCategorySlice(g.Key, g.Count()))
            .OrderByDescending(x => x.ProjectCount)
            .ThenBy(x => x.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ToListAsync(cancellationToken);

        return ongoingSeries;
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

    // END SECTION
}
