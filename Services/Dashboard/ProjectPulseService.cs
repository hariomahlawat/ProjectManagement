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
    // SECTION: Constants & fields
    private const string CacheKey = "Dashboard:ProjectPulse";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    // END SECTION

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
    // END SECTION

    // SECTION: Aggregation pipeline
    private async Task<ProjectPulseVm> BuildAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new ProjectPulseRow(
                p.LifecycleStatus,
                p.IsArchived,
                p.CreatedAt,
                p.CompletedOn))
            .ToListAsync(cancellationToken);

        var total = rows.Count;
        var completed = rows.Count(p => p.Status == ProjectLifecycleStatus.Completed);
        var ongoing = rows.Count(p => p.Status == ProjectLifecycleStatus.Active && !p.IsArchived);
        var idle = Math.Max(0, total - completed - ongoing);

        var months = BuildMonthWindows();
        var snapshots = rows.Select(r => new ProjectPulseSnapshot(
            DateOnly.FromDateTime(r.CreatedAt.Date),
            r.CompletedOn,
            r.IsArchived)).ToList();

        return new ProjectPulseVm
        {
            Total = total,
            Completed = completed,
            Ongoing = ongoing,
            Idle = idle,
            CompletedByMonth = BuildCompletedSeries(snapshots, months),
            OngoingByMonth = BuildOngoingSeries(snapshots, months),
            NewByMonth = BuildNewSeries(snapshots, months),
            RepositoryUrl = RepositoryLink,
            CompletedUrl = CompletedLink,
            OngoingUrl = OngoingLink,
            AnalyticsUrl = AnalyticsLink
        };
    }
    // END SECTION

    // SECTION: Series builders
    private static IReadOnlyList<int> BuildCompletedSeries(
        IReadOnlyList<ProjectPulseSnapshot> snapshots,
        IReadOnlyList<MonthWindow> months)
    {
        var series = new List<int>(months.Count);
        foreach (var month in months)
        {
            var count = snapshots.Count(p =>
                p.CompletedOn.HasValue &&
                p.CompletedOn.Value >= month.Start &&
                p.CompletedOn.Value < month.EndExclusive);
            series.Add(count);
        }

        return series;
    }

    private static IReadOnlyList<int> BuildOngoingSeries(
        IReadOnlyList<ProjectPulseSnapshot> snapshots,
        IReadOnlyList<MonthWindow> months)
    {
        var series = new List<int>(months.Count);
        foreach (var month in months)
        {
            var active = snapshots.Count(p =>
                !p.IsArchived &&
                p.CreatedOn < month.EndExclusive &&
                (!p.CompletedOn.HasValue || p.CompletedOn.Value >= month.EndExclusive));
            series.Add(active);
        }

        return series;
    }

    private static IReadOnlyList<int> BuildNewSeries(
        IReadOnlyList<ProjectPulseSnapshot> snapshots,
        IReadOnlyList<MonthWindow> months)
    {
        var series = new List<int>(months.Count);
        foreach (var month in months)
        {
            var count = snapshots.Count(p =>
                p.CreatedOn >= month.Start &&
                p.CreatedOn < month.EndExclusive);
            series.Add(count);
        }

        return series;
    }
    // END SECTION

    // SECTION: Month helpers
    private static IReadOnlyList<MonthWindow> BuildMonthWindows()
    {
        var current = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var months = new List<MonthWindow>(6);
        for (var i = 5; i >= 0; i--)
        {
            var start = current.AddMonths(-i);
            months.Add(new MonthWindow(start, start.AddMonths(1)));
        }

        return months;
    }
    // END SECTION

    // SECTION: Links & DTOs
    private const string CompletedLink = "/Projects?status=Completed";
    private const string OngoingLink = "/Projects?status=Ongoing";
    private const string RepositoryLink = "/Projects";
    private const string AnalyticsLink = "/Reports/Projects/Analytics";

    private sealed record ProjectPulseRow(
        ProjectLifecycleStatus Status,
        bool IsArchived,
        DateTime CreatedAt,
        DateOnly? CompletedOn);

    private sealed record ProjectPulseSnapshot(
        DateOnly CreatedOn,
        DateOnly? CompletedOn,
        bool IsArchived);

    private sealed record MonthWindow(DateOnly Start, DateOnly EndExclusive);
    // END SECTION
}
