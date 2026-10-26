using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.Dashboard.Components.OpsSignals;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IprStatusPolicy = ProjectManagement.Application.Ipr.IprStatusPolicy;

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
    private readonly IProliferationSummaryReadService _proliferationSummaryService;
    // END SECTION

    public OpsSignalsService(
        ApplicationDbContext db,
        ILogger<OpsSignalsService> log,
        IMemoryCache cache,
        IProliferationSummaryReadService proliferationSummaryService)
    {
        _db = db;
        _log = log;
        _cache = cache;
        _proliferationSummaryService = proliferationSummaryService
            ?? throw new ArgumentNullException(nameof(proliferationSummaryService));
    }

    public async Task<OpsSignalsVm> GetAsync(DateOnly? from, DateOnly? to, string userId, CancellationToken ct)
    {
        // SECTION: Range + cache setup
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = from ?? end.AddMonths(-11).AddDays(1 - end.Day);
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
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);

            var visitsSpark = months.Select(m =>
                visitsByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                             .Select(x => x.Value).FirstOrDefault()
            ).ToList();
            var visitsCount = await _db.Visits.LongCountAsync(ct);
            var visitorsTotal = await _db.Visits.SumAsync(v => (long)v.Strength, ct);
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
                .Where(TrainingReach.MayFallBetween(start, end))
                .Select(TrainingReach.Row)
                .ToListAsync(ct);

            var trainingByMonth = trainingCandidates
                .Select(candidate =>
                {
                    var referenceDate = TrainingReach.ReferenceDate(candidate);

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

            var trainingTotal = await _db.Trainings
                .Select(TrainingReach.HeadCount)
                .SumAsync(headCount => (long)headCount, ct);
            // END SECTION

            // SECTION: ToT aggregation (completed projects)
            var totCompletedQuery = _db.Projects
                .AsNoTracking()
                .Where(ProjectTotApplicabilityPolicy.EligibleProjectPredicate)
                .Where(p => p.Tot != null && p.Tot.Status == ProjectTotStatus.Completed);

            var totByMonth = await totCompletedQuery
                .Where(p => p.Tot!.CompletedOn.HasValue
                    && p.Tot.CompletedOn.Value >= start
                    && p.Tot.CompletedOn.Value <= end)
                .GroupBy(p => new { p.Tot!.CompletedOn!.Value.Year, p.Tot.CompletedOn.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Count() })
                .ToListAsync(ct);
            var totSpark = months.Select(m =>
                totByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                          .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var totTotal = await totCompletedQuery.CountAsync(ct);
            // END SECTION

            // SECTION: IPR aggregation
            // Filed and Granted records only; rejected and withdrawn filings are history.
            var countedIpr = _db.IprRecords.Where(IprStatusPolicy.CountedPredicate);
            var iprByMonth = await countedIpr
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
            var iprFiledTotal = await countedIpr.LongCountAsync(r => r.FiledAtUtc.HasValue, ct);
            var iprGrantedTotal = await countedIpr.LongCountAsync(r => r.Status == IprStatus.Granted, ct);
            // END SECTION

            // SECTION: Proliferation aggregation
            // Approved entries only, on projects that count in proliferation totals, as the
            // headline total and the Command desk count them. Annual figures carry no month,
            // so the monthly trend is built from dated detailed entries.
            var prolifProjectIds = ProliferationProjectEligibility.CountedProjects(_db.Projects).Select(p => p.Id);
            var prolifByMonth = await _db.ProliferationGranularEntries
                .Where(p => p.ApprovalStatus == ApprovalStatus.Approved
                    && p.ProliferationDate >= start && p.ProliferationDate <= end
                    && prolifProjectIds.Contains(p.ProjectId))
                .GroupBy(p => new { p.ProliferationDate.Year, p.ProliferationDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Value = g.Sum(x => x.Quantity) })
                .ToListAsync(ct);
            var prolifSpark = months.Select(m =>
                prolifByMonth.Where(x => (int)x.Year == m.Year && (int)x.Month == m.Month)
                             .Select(x => (int)x.Value).FirstOrDefault()
            ).ToList();
            var prolifSummary = await _proliferationSummaryService.GetSummaryAsync(ct);
            var prolifGrandTotal = CalculateProliferationGrandTotal(prolifSummary);
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
                    return (cur, prev, null, "None in the last 6 months");
                }

                if (prev == 0 && cur > 0)
                {
                    return (cur, prev, null, "New in the last 3 months");
                }

                if (cur == 0 && prev > 0)
                {
                    return (cur, prev, -1d, null);
                }

                if (cur == prev)
                {
                    return (cur, prev, null, "No change");
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

            // SECTION: What the change label compares: the last three months shown against the three
            // before them (the current month counts as far as it has run).
            static string MonthSpan(DateOnly first, DateOnly last)
                => first.Year == last.Year
                    ? $"{MonthLabel(first)}–{MonthLabel(last)} {last.Year}"
                    : $"{MonthLabel(first)} {first.Year}–{MonthLabel(last)} {last.Year}";
            const string deltaBasis = "vs previous 3 months";
            // A percentage or "No change" is followed by what it compares; the worded labels
            // ("New in the last 3 months", "None in the last 6 months") already say it.
            static string? Basis(double? pct, string? label, string basis)
                => pct is not null || label is null || label == "No change" ? basis : null;
            var deltaPeriods = months.Length >= 6
                ? $"{MonthSpan(months[^3], months[^1])} compared with {MonthSpan(months[^6], months[^4])}"
                : null;
            // END SECTION

            // SECTION: Tile assembly
            // Headline numbers are all-time totals; the chart shows the last 12 months.
            const string allTime = "All time";
            var labels = months.Select(m => $"{MonthLabel(m)} {m.Year}").ToList();
            var tiles = new List<OpsTileVm>
            {
                new()
                {
                    Key = "visits",
                    Label = "Visits to SDD",
                    Value = visitsCount,
                    Unit = "visits",
                    Caption = allTime,
                    SecondaryValue = visitorsTotal,
                    SecondaryLabel = "visitors",
                    TrendLabel = "Monthly visits · 12 months",
                    Sparkline = visitsSpark,
                    SparklineLabels = labels,
                    DeltaAbs = vCur - vPrev,
                    DeltaPct = vPct,
                    DeltaLabel = vLabel,
                    DeltaBasis = Basis(vPct, vLabel, deltaBasis),
                    DeltaPeriods = deltaPeriods,
                    LinkUrl = "/ProjectOfficeReports/Visits",
                    Icon = "bi-people"
                },
                new()
                {
                    Key = "outreach",
                    Label = "Social-media activities",
                    Value = outreachTotal,
                    Unit = "activities",
                    Caption = allTime,
                    Sparkline = outreachSpark,
                    SparklineLabels = labels,
                    DeltaPct = outPct,
                    DeltaLabel = outLabel,
                    DeltaBasis = Basis(outPct, outLabel, deltaBasis),
                    DeltaPeriods = deltaPeriods,
                    LinkUrl = "/ProjectOfficeReports/SocialMedia",
                    Icon = "bi-megaphone"
                },
                new()
                {
                    Key = "training",
                    Label = "Training reach",
                    Value = trainingTotal,
                    Unit = "people",
                    Caption = allTime,
                    Sparkline = trainingSpark,
                    SparklineLabels = labels,
                    DeltaPct = trPct,
                    DeltaLabel = trLabel,
                    DeltaBasis = Basis(trPct, trLabel, deltaBasis),
                    DeltaPeriods = deltaPeriods,
                    LinkUrl = "/ProjectOfficeReports/Training",
                    Icon = "bi-mortarboard"
                },
                new()
                {
                    Key = "tot",
                    Label = "ToT completed",
                    Value = totTotal,
                    Unit = "projects",
                    Caption = allTime,
                    Sparkline = totSpark,
                    SparklineLabels = labels,
                    DeltaPct = totPct,
                    DeltaLabel = totLabel,
                    DeltaBasis = Basis(totPct, totLabel, deltaBasis),
                    DeltaPeriods = deltaPeriods,
                    LinkUrl = "/ProjectOfficeReports/Tot/Summary",
                    Icon = "bi-arrow-left-right"
                },
                new()
                {
                    Key = "ipr",
                    Label = "IPR",
                    Value = iprGrantedTotal,
                    Unit = "granted",
                    Caption = allTime,
                    SecondaryValue = iprFiledTotal,
                    SecondaryLabel = "filed",
                    TrendLabel = "Monthly filings · 12 months",
                    Sparkline = iprSpark,
                    SparklineLabels = labels,
                    DeltaPct = iprPct,
                    // The headline counts grants; the chart and the change count filings.
                    DeltaLabel = iprLabel == "New in the last 3 months" ? "New filings in the last 3 months"
                        : iprLabel == "None in the last 6 months" ? "No filings in the last 6 months"
                        : iprLabel,
                    DeltaBasis = Basis(iprPct, iprLabel, "filings " + deltaBasis),
                    DeltaPeriods = deltaPeriods,
                    LinkUrl = "/ProjectOfficeReports/Ipr",
                    Icon = "bi-file-lock"
                },
                new()
                {
                    Key = "proliferation",
                    Label = "Proliferated units",
                    // Approved units from the proliferation summary; never a count of entries.
                    Value = prolifGrandTotal,
                    Unit = "units",
                    Caption = allTime,
                    Sparkline = prolifSpark,
                    SparklineLabels = labels,
                    DeltaPct = prPct,
                    DeltaLabel = prLabel,
                    DeltaBasis = Basis(prPct, prLabel, deltaBasis),
                    DeltaPeriods = deltaPeriods,
                    LinkUrl = "/ProjectOfficeReports/Proliferation/Summary",
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

    private static int CalculateProliferationGrandTotal(ProliferationSummaryViewModel? summary)
    {
        if (summary is null)
        {
            return 0;
        }

        if (summary.ByProject.Count > 0)
        {
            return summary.ByProject.Sum(row => row.Totals.Total);
        }

        if (summary.ByYear.Count > 0)
        {
            return summary.ByYear.Sum(row => row.Totals.Total);
        }

        if (summary.ByProjectYear.Count > 0)
        {
            return summary.ByProjectYear.Sum(row => row.Totals.Total);
        }

        return 0;
    }
}
