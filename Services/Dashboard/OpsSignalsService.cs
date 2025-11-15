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
                .GroupBy(v => new { v.DateOfVisit!.Value.Year, v.DateOfVisit.Value.Month })
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
                .Where(e => e.Date >= start && e.Date <= end)
                .GroupBy(e => new { e.Date!.Value.Year, e.Date.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var outreachSpark = months.Select(m =>
                outreachByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                               .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var outreachTotal = await _db.SocialMediaEvents.LongCountAsync(ct);
            // END SECTION

            // SECTION: Training aggregation
            var trainingByMonth = await _db.TrainingSessions
                .Where(t => t.Date >= start && t.Date <= end)
                .GroupBy(t => new { t.Date!.Value.Year, t.Date.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Sum(x => x.OfficerCount + x.JcoCount + x.OrCount) })
                .ToListAsync(ct);
            var trainingSpark = months.Select(m =>
                trainingByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                               .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var trainingTotal = await _db.TrainingSessions.SumAsync(t => (long)(t.OfficerCount + t.JcoCount + t.OrCount), ct);
            // END SECTION

            // SECTION: ToT aggregation
            var totByMonth = await _db.TotRecords
                .Where(r => r.Date >= start && r.Date <= end)
                .GroupBy(r => new { r.Date!.Value.Year, r.Date.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var totSpark = months.Select(m =>
                totByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                          .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var totTotal = await _db.TotRecords.LongCountAsync(ct);
            // END SECTION

            // SECTION: IPR aggregation
            var iprByMonth = await _db.IprRecords
                .Where(r => r.FiledOn >= start && r.FiledOn <= end)
                .GroupBy(r => new { r.FiledOn!.Value.Year, r.FiledOn.Value.Month })
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
            var prolifByMonth = await _db.ProliferationRecords
                .Where(p => p.Date >= start && p.Date <= end)
                .GroupBy(p => new { p.Date!.Value.Year, p.Date.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var prolifSpark = months.Select(m =>
                prolifByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                             .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var prolifTotal = await _db.ProliferationRecords.LongCountAsync(ct);
            // END SECTION

            // SECTION: Delta helper
            static (int cur, int prev, double? pct) Delta(IReadOnlyList<int> values)
            {
                if (values.Count < 6)
                {
                    return (0, 0, null);
                }

                var cur = values.Skip(Math.Max(0, values.Count - 3)).Sum();
                var prev = values.Skip(Math.Max(0, values.Count - 6)).Take(3).Sum();
                double? pct = prev == 0 ? (cur == 0 ? null : 1.0) : (cur - prev) / (double)prev;
                return (cur, prev, pct);
            }

            var (vCur, vPrev, vPct) = Delta(visitsSpark);
            var (_, _, outPct) = Delta(outreachSpark);
            var (_, _, trPct) = Delta(trainingSpark);
            var (_, _, totPct) = Delta(totSpark);
            var (_, _, iprPct) = Delta(iprSpark);
            var (_, _, prPct) = Delta(prolifSpark);
            // END SECTION

            // SECTION: Tile assembly
            var labels = months.Select(MonthLabel).ToList();
            var tiles = new List<OpsTileVm>
            {
                new()
                {
                    Key = "visits",
                    Label = "Visits (people)",
                    Value = visitsTotal,
                    Unit = string.Empty,
                    Sparkline = visitsSpark,
                    SparklineLabels = labels,
                    DeltaAbs = vCur - vPrev,
                    DeltaPct = vPct,
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
                    LinkUrl = "/ProjectOfficeReports/SocialMedia?range=last12m",
                    Icon = "bi-megaphone"
                },
                new()
                {
                    Key = "training",
                    Label = "Training (people)",
                    Value = trainingTotal,
                    Unit = "people",
                    Sparkline = trainingSpark,
                    SparklineLabels = labels,
                    DeltaPct = trPct,
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
