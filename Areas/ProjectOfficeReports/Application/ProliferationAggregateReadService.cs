using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// Produces the authoritative approved proliferation total for every project/source/year.
/// The service deliberately keeps the annual quantity and detailed quantity separate so
/// that every screen can explain how the reported total was derived.
/// </summary>
public sealed class ProliferationAggregateReadService
{
    private readonly ApplicationDbContext _db;

    public ProliferationAggregateReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ProliferationAggregateRow>> GetApprovedAggregatesAsync(
        int? projectId,
        CancellationToken cancellationToken)
    {
        var yearlyQuery = _db.ProliferationYearlies
            .AsNoTracking()
            .Where(x => x.ApprovalStatus == ApprovalStatus.Approved);

        var detailedQuery = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x => x.ApprovalStatus == ApprovalStatus.Approved);

        var preferenceQuery = _db.ProliferationYearPreferences
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
        {
            yearlyQuery = yearlyQuery.Where(x => x.ProjectId == projectId.Value);
            detailedQuery = detailedQuery.Where(x => x.ProjectId == projectId.Value);
            preferenceQuery = preferenceQuery.Where(x => x.ProjectId == projectId.Value);
        }

        var yearly = await yearlyQuery
            .GroupBy(x => new { x.ProjectId, x.Source, x.Year })
            .Select(group => new AnnualAggregate(
                group.Key.ProjectId,
                group.Key.Source,
                group.Key.Year,
                group.Sum(x => x.TotalQuantity),
                group.Max(x => x.LastUpdatedOnUtc)))
            .ToListAsync(cancellationToken);

        var detailed = await detailedQuery
            .GroupBy(x => new { x.ProjectId, x.Source, Year = x.ProliferationDate.Year })
            .Select(group => new DetailedAggregate(
                group.Key.ProjectId,
                group.Key.Source,
                group.Key.Year,
                group.Sum(x => x.Quantity),
                group.Count(),
                group.Max(x => x.LastUpdatedOnUtc)))
            .ToListAsync(cancellationToken);

        var preferences = await preferenceQuery
            .Select(x => new PreferenceAggregate(
                x.ProjectId,
                x.Source,
                x.Year,
                x.Mode,
                x.SetOnUtc))
            .ToListAsync(cancellationToken);

        var keys = yearly
            .Select(x => new AggregateKey(x.ProjectId, x.Source, x.Year))
            .Concat(detailed.Select(x => new AggregateKey(x.ProjectId, x.Source, x.Year)))
            .Distinct()
            .ToArray();

        if (keys.Length == 0)
        {
            return Array.Empty<ProliferationAggregateRow>();
        }

        var projectIds = keys.Select(x => x.ProjectId).Distinct().ToArray();
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.Id) && !x.IsDeleted && !x.IsArchived)
            .Select(x => new ProjectAggregate(
                x.Id,
                x.Name,
                x.CaseFileNumber,
                x.TechnicalCategory != null ? x.TechnicalCategory.Name : null))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var yearlyMap = yearly.ToDictionary(
            x => new AggregateKey(x.ProjectId, x.Source, x.Year));
        var detailedMap = detailed.ToDictionary(
            x => new AggregateKey(x.ProjectId, x.Source, x.Year));
        var preferenceMap = preferences.ToDictionary(
            x => new AggregateKey(x.ProjectId, x.Source, x.Year));

        var rows = new List<ProliferationAggregateRow>(keys.Length);
        foreach (var key in keys)
        {
            if (!projects.TryGetValue(key.ProjectId, out var project))
            {
                continue;
            }

            yearlyMap.TryGetValue(key, out var annual);
            detailedMap.TryGetValue(key, out var detail);
            preferenceMap.TryGetValue(key, out var preference);

            var annualQuantity = annual?.Quantity ?? 0;
            var detailedQuantity = detail?.Quantity ?? 0;
            var mode = preference?.Mode ?? GetDefaultMode(key.Source);
            var reportedTotal = CalculateReportedTotal(mode, annualQuantity, detailedQuantity);
            var latest = Latest(
                annual?.LastUpdatedOnUtc,
                detail?.LastUpdatedOnUtc,
                preference?.SetOnUtc);

            rows.Add(new ProliferationAggregateRow(
                project.Id,
                project.Name,
                project.Code,
                project.TechnicalCategoryName,
                key.Source,
                key.Year,
                annualQuantity,
                detailedQuantity,
                detail?.EntryCount ?? 0,
                mode,
                preference is not null && preference.Mode != GetDefaultMode(key.Source),
                reportedTotal,
                latest));
        }

        return rows
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Source)
            .ToList();
    }

    public async Task<ProliferationAggregateRow?> GetApprovedAggregateAsync(
        int projectId,
        ProliferationSource source,
        int year,
        CancellationToken cancellationToken)
    {
        var annual = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                x.Source == source &&
                x.Year == year &&
                x.ApprovalStatus == ApprovalStatus.Approved)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Quantity = group.Sum(x => x.TotalQuantity),
                LastUpdatedOnUtc = (DateTime?)group.Max(x => x.LastUpdatedOnUtc)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var detailed = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                x.Source == source &&
                x.ProliferationDate.Year == year &&
                x.ApprovalStatus == ApprovalStatus.Approved)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Quantity = group.Sum(x => x.Quantity),
                EntryCount = group.Count(),
                LastUpdatedOnUtc = (DateTime?)group.Max(x => x.LastUpdatedOnUtc)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (annual is null && detailed is null)
        {
            return null;
        }

        var preference = await _db.ProliferationYearPreferences
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.Year == year)
            .Select(x => new { x.Mode, x.SetOnUtc })
            .FirstOrDefaultAsync(cancellationToken);

        var project = await _db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Name,
                Code = x.CaseFileNumber,
                TechnicalCategoryName = x.TechnicalCategory != null ? x.TechnicalCategory.Name : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        var annualQuantity = annual?.Quantity ?? 0;
        var detailedQuantity = detailed?.Quantity ?? 0;
        var mode = preference?.Mode ?? GetDefaultMode(source);

        return new ProliferationAggregateRow(
            projectId,
            project?.Name ?? $"Project {projectId}",
            project?.Code,
            project?.TechnicalCategoryName,
            source,
            year,
            annualQuantity,
            detailedQuantity,
            detailed?.EntryCount ?? 0,
            mode,
            preference is not null && preference.Mode != GetDefaultMode(source),
            CalculateReportedTotal(mode, annualQuantity, detailedQuantity),
            Latest(annual?.LastUpdatedOnUtc, detailed?.LastUpdatedOnUtc, preference?.SetOnUtc));
    }

    public static YearPreferenceMode GetDefaultMode(ProliferationSource source) => source switch
    {
        ProliferationSource.Abw515 => YearPreferenceMode.UseYearly,
        _ => YearPreferenceMode.UseYearlyAndGranular
    };

    public static int CalculateReportedTotal(
        YearPreferenceMode mode,
        int annualQuantity,
        int detailedQuantity) => mode switch
    {
        YearPreferenceMode.UseYearly => annualQuantity,
        YearPreferenceMode.UseGranular => detailedQuantity,
        YearPreferenceMode.Auto => detailedQuantity > 0 ? detailedQuantity : annualQuantity,
        YearPreferenceMode.UseYearlyAndGranular => checked(annualQuantity + detailedQuantity),
        _ => checked(annualQuantity + detailedQuantity)
    };

    public static string GetCalculationLabel(
        YearPreferenceMode mode,
        ProliferationSource source) => mode switch
    {
        YearPreferenceMode.UseYearly => "Annual quantity only",
        YearPreferenceMode.UseGranular => "Detailed entries only",
        YearPreferenceMode.Auto => "Detailed entries where available; otherwise annual quantity",
        YearPreferenceMode.UseYearlyAndGranular => "Annual quantity + detailed entries",
        _ => source == ProliferationSource.Abw515
            ? "Annual quantity only"
            : "Annual quantity + detailed entries"
    };

    private static DateTime? Latest(params DateTime?[] values)
    {
        DateTime? latest = null;
        foreach (var value in values)
        {
            if (value.HasValue && (!latest.HasValue || value.Value > latest.Value))
            {
                latest = value.Value;
            }
        }

        return latest;
    }

    private readonly record struct AggregateKey(int ProjectId, ProliferationSource Source, int Year);

    private sealed record AnnualAggregate(
        int ProjectId,
        ProliferationSource Source,
        int Year,
        int Quantity,
        DateTime LastUpdatedOnUtc);

    private sealed record DetailedAggregate(
        int ProjectId,
        ProliferationSource Source,
        int Year,
        int Quantity,
        int EntryCount,
        DateTime LastUpdatedOnUtc);

    private sealed record PreferenceAggregate(
        int ProjectId,
        ProliferationSource Source,
        int Year,
        YearPreferenceMode Mode,
        DateTime SetOnUtc);

    private sealed record ProjectAggregate(
        int Id,
        string Name,
        string? Code,
        string? TechnicalCategoryName);
}

public sealed record ProliferationAggregateRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    string? TechnicalCategoryName,
    ProliferationSource Source,
    int Year,
    int AnnualQuantity,
    int DetailedQuantity,
    int DetailedEntryCount,
    YearPreferenceMode EffectiveMode,
    bool HasCountingException,
    int ReportedTotal,
    DateTime? LastUpdatedOnUtc)
{
    public string SourceLabel => Source.ToDisplayName();

    public string CalculationLabel =>
        ProliferationAggregateReadService.GetCalculationLabel(EffectiveMode, Source);
}
