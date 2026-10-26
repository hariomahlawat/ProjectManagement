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
            .Where(p =>
                (p.Source == ProliferationSource.Sdd && p.Mode != YearPreferenceMode.UseYearlyAndGranular) ||
                (p.Source == ProliferationSource.Abw515 && p.Mode != YearPreferenceMode.UseYearly));

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

        var orderedQuery = query.OrderByDescending(pref => pref.SetOnUtc);

        var baseQuery = from pref in orderedQuery
                        join project in _db.Projects.AsNoTracking() on pref.ProjectId equals project.Id
                        where !project.IsDeleted && !project.IsArchived
                        join user in _db.Users.AsNoTracking() on pref.SetByUserId equals user.Id into userJoin
                        from user in userJoin.DefaultIfEmpty()
                        select new
                        {
                            Preference = pref,
                            ProjectName = project.Name,
                            ProjectCode = project.CaseFileNumber,
                            SetByFullName = user != null ? user.FullName : null,
                            SetByUserName = user != null ? user.UserName : null
                        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var trimmed = request.Search.Trim();
            if (_db.Database.IsNpgsql())
            {
                var like = $"%{trimmed}%";
                baseQuery = baseQuery.Where(x =>
                    EF.Functions.ILike(x.ProjectName, like) ||
                    (x.ProjectCode != null && EF.Functions.ILike(x.ProjectCode, like)) ||
                    (x.SetByFullName != null && EF.Functions.ILike(x.SetByFullName, like)) ||
                    (x.SetByUserName != null && EF.Functions.ILike(x.SetByUserName, like)));
            }
            else
            {
                var normalized = trimmed.ToLowerInvariant();
                baseQuery = baseQuery.Where(x =>
                    x.ProjectName.ToLower().Contains(normalized) ||
                    (x.ProjectCode != null && x.ProjectCode.ToLower().Contains(normalized)) ||
                    (x.SetByFullName != null && x.SetByFullName.ToLower().Contains(normalized)) ||
                    (x.SetByUserName != null && x.SetByUserName.ToLower().Contains(normalized)));
            }
        }

        // Apply filters to a translatable projection before constructing the
        // result record; EF cannot push predicates through record constructors.
        var projections = await baseQuery
            .Select(x => new PreferenceProjection(
                x.Preference, x.ProjectName, x.ProjectCode, x.SetByFullName, x.SetByUserName))
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
            .Where(y => projectIds.Contains(y.ProjectId) &&
                        sources.Contains(y.Source) &&
                        years.Contains(y.Year))
            .GroupBy(y => new { y.ProjectId, y.Source, y.Year })
            .Select(g => new
            {
                g.Key.ProjectId,
                g.Key.Source,
                g.Key.Year,
                ApprovedTotal = g.Sum(y => y.ApprovalStatus == ApprovalStatus.Approved ? y.TotalQuantity : 0),
                ApprovedCount = g.Count(y => y.ApprovalStatus == ApprovalStatus.Approved),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var granularTotals = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g => projectIds.Contains(g.ProjectId) &&
                        sources.Contains(g.Source) &&
                        years.Contains(g.ProliferationDate.Year))
            .GroupBy(g => new { g.ProjectId, g.Source, Year = g.ProliferationDate.Year })
            .Select(g => new
            {
                g.Key.ProjectId,
                g.Key.Source,
                g.Key.Year,
                ApprovedTotal = g.Sum(x => x.ApprovalStatus == ApprovalStatus.Approved ? x.Quantity : 0),
                ApprovedCount = g.Count(x => x.ApprovalStatus == ApprovalStatus.Approved),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var yearlyLookup = yearlyTotals.ToDictionary(
            x => new PreferenceKey(x.ProjectId, x.Source, x.Year),
            x => new AggregateTotals(x.ApprovedTotal, x.Count > 0, x.ApprovedCount > 0));

        var granularLookup = granularTotals.ToDictionary(
            x => new PreferenceKey(x.ProjectId, x.Source, x.Year),
            x => new AggregateTotals(x.ApprovedTotal, x.Count > 0, x.ApprovedCount > 0));

        var effectiveTotals = new Dictionary<PreferenceKey, int>(combos.Count);
        foreach (var combo in combos)
        {
            try
            {
                var total = await _trackerReadService.GetEffectiveTotalAsync(
                    combo.ProjectId,
                    combo.Source,
                    combo.Year,
                    cancellationToken);
                effectiveTotals[combo] = total;
            }
            catch
            {
                // ignore failures and fall back to zero
            }
        }

        var results = projections
            .Select(x =>
            {
                var key = new PreferenceKey(x.Preference.ProjectId, x.Preference.Source, x.Preference.Year);
                var yearlyInfo = yearlyLookup.TryGetValue(key, out var y) ? y : default;
                var granularInfo = granularLookup.TryGetValue(key, out var g) ? g : default;
                var effective = ResolveEffectiveMode(x.Preference.Mode, yearlyInfo, granularInfo);
                var displayName = BuildDisplayName(x.SetByFullName, x.SetByUserName, x.Preference.SetByUserId);
                var effectiveTotal = effectiveTotals.TryGetValue(key, out var total) ? total : 0;

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
                    granularInfo.HasAny,
                    yearlyInfo.HasApproved,
                    granularInfo.HasApproved,
                    effectiveTotal);
            })
            .ToList();

        return results;
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

    private readonly record struct AggregateTotals(int Total, bool HasAny, bool HasApproved);

    private readonly record struct PreferenceKey(int ProjectId, ProliferationSource Source, int Year);
}
