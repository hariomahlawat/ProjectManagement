using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationOverviewService
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationTrackerReadService _trackerReadService;

    public ProliferationOverviewService(ApplicationDbContext db, ProliferationTrackerReadService trackerReadService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerReadService = trackerReadService ?? throw new ArgumentNullException(nameof(trackerReadService));
    }

    public async Task<ProliferationOverviewResponse> GetOverviewAsync(
        ProliferationOverviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 20 : Math.Min(request.PageSize, 100);

        var projectsQuery = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

        if (request.ProjectCategoryId.HasValue)
        {
            projectsQuery = projectsQuery.Where(p => p.CategoryId == request.ProjectCategoryId.Value);
        }

        if (request.TechnicalCategoryId.HasValue)
        {
            projectsQuery = projectsQuery.Where(p => p.TechnicalCategoryId == request.TechnicalCategoryId.Value);
        }

        var projectInfos = await projectsQuery
            .Select(p => new ProjectInfo(p.Id, p.Name, p.CaseFileNumber))
            .ToListAsync(cancellationToken);

        if (projectInfos.Count == 0)
        {
            return new ProliferationOverviewResponse(
                EmptySummary(),
                new PagedResult<ProliferationOverviewRow>(Array.Empty<ProliferationOverviewRow>(), 0, page, pageSize));
        }

        var projectIds = projectInfos.Select(p => p.Id).ToArray();
        var projectLookup = projectInfos.ToDictionary(p => p.Id);

        var yearsFilter = request.Years is { Count: > 0 } ? new HashSet<int>(request.Years) : null;
        var likeTerm = NormalizeSearch(request.Search);

        var yearlyQuery = _db.ProliferationYearlies
            .AsNoTracking()
            .Where(y => projectIds.Contains(y.ProjectId));

        var granularQuery = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g => projectIds.Contains(g.ProjectId));

        if (request.Source.HasValue)
        {
            yearlyQuery = yearlyQuery.Where(y => y.Source == request.Source.Value);
            granularQuery = granularQuery.Where(g => g.Source == request.Source.Value);
        }

        if (yearsFilter is not null)
        {
            yearlyQuery = yearlyQuery.Where(y => yearsFilter.Contains(y.Year));
            granularQuery = granularQuery.Where(g => yearsFilter.Contains(g.ProliferationDate.Year));
        }
        else
        {
            if (request.DateFrom.HasValue)
            {
                var from = request.DateFrom.Value;
                yearlyQuery = yearlyQuery.Where(y => y.Year >= from.Year);
                granularQuery = granularQuery.Where(g => g.ProliferationDate >= from);
            }

            if (request.DateTo.HasValue)
            {
                var to = request.DateTo.Value;
                yearlyQuery = yearlyQuery.Where(y => y.Year <= to.Year);
                granularQuery = granularQuery.Where(g => g.ProliferationDate <= to);
            }
        }

        var yearlyItems = await yearlyQuery.ToListAsync(cancellationToken);
        var granularItems = await granularQuery.ToListAsync(cancellationToken);

        if (!string.IsNullOrEmpty(likeTerm))
        {
            yearlyItems = yearlyItems
                .Where(y => MatchesSearch(projectLookup[y.ProjectId], likeTerm) ||
                            (!string.IsNullOrWhiteSpace(y.Remarks) && y.Remarks!.Contains(likeTerm, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            granularItems = granularItems
                .Where(g => MatchesSearch(projectLookup[g.ProjectId], likeTerm) ||
                            g.UnitName.Contains(likeTerm, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(g.Remarks) && g.Remarks!.Contains(likeTerm, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var preferenceLookup = await _db.ProliferationYearPreferences
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.ProjectId))
            .ToDictionaryAsync(
                p => new PreferenceKey(p.ProjectId, p.Source, p.Year),
                p => p.Mode,
                cancellationToken);

        var gridRows = BuildGridRows(projectLookup, yearlyItems, granularItems, preferenceLookup);

        var combos = yearlyItems
            .Select(y => new Combination(y.ProjectId, y.Source, y.Year))
            .Concat(granularItems.Select(g => new Combination(g.ProjectId, g.Source, g.ProliferationDate.Year)))
            .Distinct()
            .ToList();

        var totals = new List<CombinationTotal>(combos.Count);
        foreach (var combo in combos)
        {
            var total = await _trackerReadService.GetEffectiveTotalAsync(combo.ProjectId, combo.Source, combo.Year, cancellationToken);
            totals.Add(new CombinationTotal(combo.ProjectId, combo.Source, combo.Year, total));
        }

        var totalLookup = totals.ToDictionary(t => new Combination(t.ProjectId, t.Source, t.Year), t => t.Total);
        for (var i = 0; i < gridRows.Count; i++)
        {
            var row = gridRows[i];
            if (totalLookup.TryGetValue(new Combination(row.ProjectId, row.Source, row.Year), out var total))
            {
                gridRows[i] = row with { EffectiveTotal = total };
            }
        }

        var totalRows = gridRows.Count;
        var orderedRows = gridRows
            .OrderByDescending(r => r.Year)
            .ThenBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Source)
            .ThenBy(r => r.DataType)
            .ThenBy(r => r.ProliferationDate ?? DateOnly.MinValue)
            .ToList();

        var pagedItems = orderedRows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var summary = BuildSummary(totals);

        return new ProliferationOverviewResponse(
            summary,
            new PagedResult<ProliferationOverviewRow>(pagedItems, totalRows, page, pageSize));
    }

    public async Task<IReadOnlyList<ProliferationPreferenceOverrideItem>> GetPreferenceOverridesAsync(
        ProliferationPreferenceOverrideRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var query = _db.ProliferationYearPreferences
            .AsNoTracking()
            .Where(p => p.Mode != YearPreferenceMode.UseYearlyAndGranular);

        if (request.ProjectId.HasValue)
        {
            query = query.Where(p => p.ProjectId == request.ProjectId.Value);
        }

        if (request.Source.HasValue)
        {
            query = query.Where(p => p.Source == request.Source.Value);
        }

        if (request.Year.HasValue)
        {
            query = query.Where(p => p.Year == request.Year.Value);
        }

        var baseQuery = from pref in query
                        join project in _db.Projects.AsNoTracking() on pref.ProjectId equals project.Id
                        where !project.IsDeleted && !project.IsArchived
                        join user in _db.Users.AsNoTracking() on pref.SetByUserId equals user.Id into userJoin
                        from user in userJoin.DefaultIfEmpty()
                        select new PreferenceProjection(
                            pref,
                            project.Name,
                            project.CaseFileNumber,
                            user != null ? user.FullName : null,
                            user != null ? user.UserName : null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var trimmed = request.Search.Trim();
            var like = $"%{trimmed}%";
            baseQuery = baseQuery.Where(x =>
                EF.Functions.ILike(x.ProjectName, like) ||
                (x.ProjectCode != null && EF.Functions.ILike(x.ProjectCode, like)) ||
                (x.SetByFullName != null && EF.Functions.ILike(x.SetByFullName, like)) ||
                (x.SetByUserName != null && EF.Functions.ILike(x.SetByUserName, like)));
        }

        var projections = await baseQuery
            .OrderByDescending(x => x.Preference.SetOnUtc)
            .ToListAsync(cancellationToken);

        if (projections.Count == 0)
        {
            return Array.Empty<ProliferationPreferenceOverrideItem>();
        }

        var combos = projections
            .Select(x => new PreferenceKey(x.Preference.ProjectId, x.Preference.Source, x.Preference.Year))
            .Distinct()
            .ToList();

        if (combos.Count == 0)
        {
            return Array.Empty<ProliferationPreferenceOverrideItem>();
        }

        var projectIds = combos.Select(x => x.ProjectId).Distinct().ToArray();
        var sources = combos.Select(x => x.Source).Distinct().ToArray();
        var years = combos.Select(x => x.Year).Distinct().ToArray();

        var yearlyTotals = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(y => y.ApprovalStatus == ApprovalStatus.Approved &&
                        projectIds.Contains(y.ProjectId) &&
                        sources.Contains(y.Source) &&
                        years.Contains(y.Year))
            .GroupBy(y => new { y.ProjectId, y.Source, y.Year })
            .Select(g => new
            {
                g.Key.ProjectId,
                g.Key.Source,
                g.Key.Year,
                Total = g.Sum(y => y.TotalQuantity),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var granularTotals = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g => g.ApprovalStatus == ApprovalStatus.Approved &&
                        projectIds.Contains(g.ProjectId) &&
                        sources.Contains(g.Source) &&
                        years.Contains(g.ProliferationDate.Year))
            .GroupBy(g => new { g.ProjectId, g.Source, Year = g.ProliferationDate.Year })
            .Select(g => new
            {
                g.Key.ProjectId,
                g.Key.Source,
                g.Key.Year,
                Total = g.Sum(x => x.Quantity),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var yearlyLookup = yearlyTotals.ToDictionary(
            x => new PreferenceKey(x.ProjectId, x.Source, x.Year),
            x => new AggregateTotals(x.Total, x.Count > 0));

        var granularLookup = granularTotals.ToDictionary(
            x => new PreferenceKey(x.ProjectId, x.Source, x.Year),
            x => new AggregateTotals(x.Total, x.Count > 0));

        var results = projections
            .Select(x =>
            {
                var key = new PreferenceKey(x.Preference.ProjectId, x.Preference.Source, x.Preference.Year);
                var yearlyInfo = yearlyLookup.TryGetValue(key, out var y) ? y : default;
                var granularInfo = granularLookup.TryGetValue(key, out var g) ? g : default;
                var effective = ResolveEffectiveMode(x.Preference.Mode, yearlyInfo, granularInfo);
                var displayName = BuildDisplayName(x.SetByFullName, x.SetByUserName, x.Preference.SetByUserId);

                return new ProliferationPreferenceOverrideItem(
                    x.Preference.Id,
                    x.Preference.ProjectId,
                    x.ProjectName,
                    x.ProjectCode,
                    x.Preference.Source,
                    x.Preference.Year,
                    x.Preference.Mode,
                    x.Preference.SetByUserId,
                    displayName,
                    x.Preference.SetOnUtc,
                    effective,
                    yearlyInfo.HasAny,
                    granularInfo.HasAny);
            })
            .ToList();

        return results;
    }

    private static ProliferationOverviewSummary BuildSummary(IReadOnlyCollection<CombinationTotal> totals)
    {
        var active = totals.Where(t => t.Total > 0).ToList();
        var now = DateTime.UtcNow;
        var thresholdYear = now.AddYears(-1).Year;
        var recent = active.Where(t => t.Year >= thresholdYear).ToList();

        return new ProliferationOverviewSummary(
            BuildKpiSet(active),
            BuildKpiSet(recent));
    }

    private static ProliferationOverviewKpiSet BuildKpiSet(IReadOnlyCollection<CombinationTotal> totals)
    {
        if (totals.Count == 0)
        {
            return new ProliferationOverviewKpiSet(0, 0, 0, 0);
        }

        var projects = totals.Select(t => t.ProjectId).Distinct().Count();
        var total = totals.Sum(t => t.Total);
        var sdd = totals.Where(t => t.Source == ProliferationSource.Sdd).Sum(t => t.Total);
        var abw = totals.Where(t => t.Source == ProliferationSource.Abw515).Sum(t => t.Total);

        return new ProliferationOverviewKpiSet(projects, total, sdd, abw);
    }

    private static List<ProliferationOverviewRow> BuildGridRows(
        IReadOnlyDictionary<int, ProjectInfo> projectLookup,
        IReadOnlyCollection<ProliferationYearly> yearly,
        IReadOnlyCollection<ProliferationGranular> granular,
        IReadOnlyDictionary<PreferenceKey, YearPreferenceMode> preferences)
    {
        var rows = new List<ProliferationOverviewRow>(yearly.Count + granular.Count);

        foreach (var item in yearly)
        {
            var project = projectLookup[item.ProjectId];
            preferences.TryGetValue(new PreferenceKey(item.ProjectId, item.Source, item.Year), out var mode);

            rows.Add(new ProliferationOverviewRow(
                item.ProjectId,
                item.Year,
                project.Name,
                project.Code,
                item.Source,
                "Yearly",
                null,
                null,
                item.TotalQuantity,
                0,
                item.ApprovalStatus,
                mode,
                item.Id));
        }

        foreach (var item in granular)
        {
            var project = projectLookup[item.ProjectId];

            rows.Add(new ProliferationOverviewRow(
                item.ProjectId,
                item.ProliferationDate.Year,
                project.Name,
                project.Code,
                item.Source,
                "Granular",
                item.UnitName,
                item.ProliferationDate,
                item.Quantity,
                0,
                item.ApprovalStatus,
                null,
                item.Id));
        }

        return rows;
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        return search.Trim();
    }

    private static bool MatchesSearch(ProjectInfo project, string term)
    {
        if (project.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(project.Code) && project.Code!.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static ProliferationOverviewSummary EmptySummary()
    {
        return new ProliferationOverviewSummary(
            new ProliferationOverviewKpiSet(0, 0, 0, 0),
            new ProliferationOverviewKpiSet(0, 0, 0, 0));
    }

    private static YearPreferenceMode ResolveEffectiveMode(
        YearPreferenceMode configured,
        AggregateTotals yearly,
        AggregateTotals granular)
    {
        return configured switch
        {
            YearPreferenceMode.Auto => granular.Total > 0 ? YearPreferenceMode.UseGranular : YearPreferenceMode.UseYearly,
            YearPreferenceMode.UseYearly => YearPreferenceMode.UseYearly,
            YearPreferenceMode.UseGranular => YearPreferenceMode.UseGranular,
            YearPreferenceMode.UseYearlyAndGranular => granular.HasAny && yearly.HasAny
                ? YearPreferenceMode.UseYearlyAndGranular
                : granular.HasAny
                    ? YearPreferenceMode.UseGranular
                    : YearPreferenceMode.UseYearly,
            _ => configured
        };
    }

    private static string BuildDisplayName(string? fullName, string? userName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName!;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName!;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Unknown" : fallback;
    }

    private readonly record struct PreferenceProjection(
        ProliferationYearPreference Preference,
        string ProjectName,
        string? ProjectCode,
        string? SetByFullName,
        string? SetByUserName);

    private readonly record struct AggregateTotals(int Total, bool HasAny);

    private readonly record struct ProjectInfo(int Id, string Name, string? Code);

    private readonly record struct Combination(int ProjectId, ProliferationSource Source, int Year);

    private readonly record struct CombinationTotal(int ProjectId, ProliferationSource Source, int Year, int Total);

    private readonly record struct PreferenceKey(int ProjectId, ProliferationSource Source, int Year);
}
