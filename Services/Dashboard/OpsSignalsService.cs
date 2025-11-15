using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.Dashboard.Components.OpsSignals;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Dashboard;

public interface IOpsSignalsService
{
    Task<OpsSignalsVm> GetAsync(DateOnly? from, DateOnly? to, string userId, CancellationToken ct);
}

public sealed class OpsSignalsService : IOpsSignalsService
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly ILogger<OpsSignalsService> _log;
    private readonly IMemoryCache _cache;
    // END SECTION

    public OpsSignalsService(ApplicationDbContext db, ILogger<OpsSignalsService> log, IMemoryCache cache)
    {
        _db = db;
        _log = log;
        _cache = cache;
    }

    public async Task<OpsSignalsVm> GetAsync(DateOnly? from, DateOnly? to, string userId, CancellationToken ct)
    {
        // SECTION: Range + cache setup
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = from ?? end.AddMonths(-11).AddDays(1 - end.Day);
        var startMonthIndex = start.Year * 12 + start.Month;
        var endMonthIndex = end.Year * 12 + end.Month;
        var startOfDay = start.ToDateTime(TimeOnly.MinValue);
        var endOfDay = end.ToDateTime(TimeOnly.MaxValue);
        var startOfDayOffset = new DateTimeOffset(startOfDay, TimeSpan.Zero);
        var endOfDayOffset = new DateTimeOffset(endOfDay, TimeSpan.Zero);
        var cacheKey = $"ops-signals:{start}:{end}";
        if (_cache.TryGetValue(cacheKey, out OpsSignalsVm? cached) && cached is not null)
        {
            return cached;
        }
        // END SECTION

        // SECTION: Helpers
        static string MonthLabel(DateOnly date)
            => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames[date.Month - 1];

        var months = Enumerable.Range(0, 12)
            .Select(i => new DateOnly(start.Year, start.Month, 1).AddMonths(i))
            .ToArray();
        // END SECTION

        try
        {
            // SECTION: Visits aggregation
            var visitsByMonth = await _db.Visits
                .Where(v => v.DateOfVisit >= start && v.DateOfVisit <= end)
                .GroupBy(v => new { v.DateOfVisit.Year, v.DateOfVisit.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Sum(x => x.Strength) })
                .ToListAsync(ct);

            var visitsSpark = months.Select(m =>
                visitsByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                             .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var visitsTotal = await _db.Visits.SumAsync(v => (long)v.Strength, ct);
            // END SECTION

            // SECTION: Social media outreach aggregation
            var outreachByMonth = await _db.SocialMediaEvents
                .Where(e => e.DateOfEvent >= start && e.DateOfEvent <= end)
                .GroupBy(e => new { e.DateOfEvent.Year, e.DateOfEvent.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var outreachSpark = months.Select(m =>
                outreachByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                               .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var outreachTotal = await _db.SocialMediaEvents.LongCountAsync(ct);
            // END SECTION

            // SECTION: Training aggregation
            var trainingCandidates = await _db.Trainings
                .AsNoTracking()
                .Where(t =>
                    (t.StartDate.HasValue && t.StartDate.Value >= start && t.StartDate.Value <= end)
                    || (t.EndDate.HasValue && t.EndDate.Value >= start && t.EndDate.Value <= end)
                    || (!t.StartDate.HasValue && !t.EndDate.HasValue
                        && t.TrainingYear.HasValue && t.TrainingMonth.HasValue
                        && (t.TrainingYear.Value * 12 + t.TrainingMonth.Value) >= startMonthIndex
                        && (t.TrainingYear.Value * 12 + t.TrainingMonth.Value) <= endMonthIndex))
                .Select(t => new
                {
                    t.StartDate,
                    t.EndDate,
                    t.TrainingYear,
                    t.TrainingMonth,
                    Total = t.Counters != null
                        ? t.Counters.Total
                        : t.LegacyOfficerCount + t.LegacyJcoCount + t.LegacyOrCount
                })
                .ToListAsync(ct);

            var trainingByMonth = trainingCandidates
                .Select(candidate =>
                {
                    var referenceDate = candidate.StartDate
                        ?? candidate.EndDate
                        ?? (candidate.TrainingYear.HasValue && candidate.TrainingMonth.HasValue
                            ? new DateOnly(candidate.TrainingYear.Value, candidate.TrainingMonth.Value, 1)
                            : (DateOnly?)null);

                    return referenceDate is { } reference && reference >= start && reference <= end
                        ? new { Date = reference, candidate.Total }
                        : null;
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .GroupBy(x => new { x.Date.Year, x.Date.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Sum(x => x.Total) })
                .ToList();

            var trainingSpark = months.Select(m =>
                trainingByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                               .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();

            var trainingTotal = await _db.Trainings.SumAsync(
                t => (long)(t.Counters != null
                    ? t.Counters.Total
                    : t.LegacyOfficerCount + t.LegacyJcoCount + t.LegacyOrCount),
                ct);
            // END SECTION

            // SECTION: ToT aggregation
            var totByMonth = await _db.ProjectTotRequests
                .Where(r => r.SubmittedOnUtc >= startOfDay && r.SubmittedOnUtc <= endOfDay)
                .GroupBy(r => new { r.SubmittedOnUtc.Year, r.SubmittedOnUtc.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var totSpark = months.Select(m =>
                totByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                          .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var totTotal = await _db.ProjectTotRequests.LongCountAsync(ct);
            // END SECTION

            // SECTION: IPR aggregation
            var iprByMonth = await _db.IprRecords
                .Where(r => r.FiledAtUtc.HasValue
                            && r.FiledAtUtc.Value >= startOfDayOffset
                            && r.FiledAtUtc.Value <= endOfDayOffset)
                .GroupBy(r => new { r.FiledAtUtc!.Value.Year, r.FiledAtUtc.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var iprSpark = months.Select(m =>
                iprByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                          .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var iprTotal = await _db.IprRecords.LongCountAsync(ct);
            var iprGranted = await _db.IprRecords.LongCountAsync(r => r.Status == IprStatus.Granted, ct);
            // END SECTION

            // SECTION: Proliferation aggregation
            var prolifByMonth = await _db.ProliferationGranularEntries
                .Where(p => p.ProliferationDate >= start && p.ProliferationDate <= end)
                .GroupBy(p => new { p.ProliferationDate.Year, p.ProliferationDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var prolifSpark = months.Select(m =>
                prolifByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                             .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var prolifTotal = await _db.ProliferationGranularEntries.LongCountAsync(ct);
            // END SECTION

            // SECTION: Delta helper
            static (int cur, int prev, double? pct, string? label) Delta(IReadOnlyList<int> values)
            {
                if (values.Count < 6)
                {
                    return (0, 0, null, null);
                }

                var cur = values.Skip(Math.Max(0, values.Count - 3)).Sum();
                var prev = values.Skip(Math.Max(0, values.Count - 6)).Take(3).Sum();
                if (cur == 0 && prev == 0)
                {
                    return (cur, prev, null, null);
                }

                if (cur == 0 || prev == 0)
                {
                    var noBaselineLabel = prev == 0 && cur > 0 ? "—" : null;
                    return (cur, prev, null, noBaselineLabel);
                }

                double? pct = (cur - prev) / (double)prev;
                return (cur, prev, pct, null);
            }

            var (vCur, vPrev, vPct, vLabel) = Delta(visitsSpark);
            var (_, _, outPct, outLabel) = Delta(outreachSpark);
            var (_, _, trPct, trLabel) = Delta(trainingSpark);
            var (_, _, totPct, totLabel) = Delta(totSpark);
            var (_, _, iprPct, iprLabel) = Delta(iprSpark);
            var (_, _, prPct, prLabel) = Delta(prolifSpark);
            // END SECTION

            // SECTION: Tile assembly
            var labels = months.Select(MonthLabel).ToList();
            var tiles = new List<OpsTileVm>
            {
                new()
                {
                    Key = "visits",
                    Label = "Visits",
                    Value = visitsTotal,
                    Unit = "people",
                    Sparkline = visitsSpark,
                    SparklineLabels = labels,
                    DeltaAbs = vCur - vPrev,
                    DeltaPct = vPct,
                    DeltaLabel = vLabel,
                    LinkUrl = "/ProjectOfficeReports/Visits?range=last12m",
                    Icon = "bi-people"
                },
                new()
                {
                    Key = "outreach",
                    Label = "Social media outreach",
                    Value = outreachTotal,
                    Sparkline = outreachSpark,
                    SparklineLabels = labels,
                    DeltaPct = outPct,
                    DeltaLabel = outLabel,
                    LinkUrl = "/ProjectOfficeReports/SocialMedia?range=last12m",
                    Icon = "bi-megaphone"
                },
                new()
                {
                    Key = "training",
                    Label = "Training",
                    Value = trainingTotal,
                    Unit = "people",
                    Sparkline = trainingSpark,
                    SparklineLabels = labels,
                    DeltaPct = trPct,
                    DeltaLabel = trLabel,
                    LinkUrl = "/ProjectOfficeReports/Training?range=last12m",
                    Icon = "bi-mortarboard"
                },
                new()
                {
                    Key = "tot",
                    Label = "ToT",
                    Value = totTotal,
                    Sparkline = totSpark,
                    SparklineLabels = labels,
                    DeltaPct = totPct,
                    DeltaLabel = totLabel,
                    LinkUrl = "/ProjectOfficeReports/Tot",
                    Icon = "bi-arrow-left-right"
                },
                new()
                {
                    Key = "ipr",
                    Label = "Patents",
                    Value = iprTotal,
                    Caption = $"{iprGranted} granted",
                    Sparkline = iprSpark,
                    SparklineLabels = labels,
                    DeltaPct = iprPct,
                    DeltaLabel = iprLabel,
                    LinkUrl = "/Ipr",
                    Icon = "bi-file-lock"
                },
                new()
                {
                    Key = "proliferation",
                    Label = "Proliferation",
                    Value = prolifTotal,
                    Sparkline = prolifSpark,
                    SparklineLabels = labels,
                    DeltaPct = prPct,
                    DeltaLabel = prLabel,
                    LinkUrl = "/ProjectOfficeReports/Proliferation",
                    Icon = "bi-globe2"
                }
            };
            // END SECTION

            var vm = new OpsSignalsVm
            {
                Tiles = tiles,
                RangeStart = start,
                RangeEnd = end
            };

            _cache.Set(cacheKey, vm, TimeSpan.FromSeconds(120));
            return vm;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to build OpsSignals widget for user {UserId}", userId);
            return new OpsSignalsVm
            {
                Tiles = Array.Empty<OpsTileVm>(),
                RangeStart = start,
                RangeEnd = end
            };
        }
    }
}
