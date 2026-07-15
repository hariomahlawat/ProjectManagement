using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationProjectReadService : IProliferationProjectReadService
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationAggregateReadService _aggregateReadService;

    public ProliferationProjectReadService(
        ApplicationDbContext db,
        ProliferationAggregateReadService aggregateReadService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
    }

    public async Task<ProliferationProjectDetailViewModel?> GetProjectAsync(
        int projectId,
        CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId && !x.IsDeleted && !x.IsArchived)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.CaseFileNumber,
                TechnicalCategoryName = x.TechnicalCategory != null
                    ? x.TechnicalCategory.Name
                    : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return null;
        }

        var aggregates = await _aggregateReadService.GetApprovedAggregatesAsync(
            projectId,
            cancellationToken);

        var annualRemarks = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.ApprovalStatus == ApprovalStatus.Approved)
            .OrderByDescending(x => x.LastUpdatedOnUtc)
            .Select(x => new
            {
                x.Source,
                x.Year,
                x.Remarks
            })
            .ToListAsync(cancellationToken);

        var annualRemarksLookup = annualRemarks
            .GroupBy(x => new SourceYearKey(x.Source, x.Year))
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Remarks).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));

        var detailedEntries = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.ApprovalStatus == ApprovalStatus.Approved)
            .OrderByDescending(x => x.ProliferationDate)
            .ThenBy(x => x.UnitName)
            .Select(x => new DetailedEntryProjection(
                x.Id,
                x.Source,
                x.ProliferationDate.Year,
                x.ProliferationDate,
                x.UnitName,
                x.Quantity,
                x.Remarks))
            .ToListAsync(cancellationToken);

        var detailedLookup = detailedEntries
            .GroupBy(x => new SourceYearKey(x.Source, x.Year))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProliferationProjectDetailedEntryViewModel>)group
                    .Select(x => new ProliferationProjectDetailedEntryViewModel(
                        x.Id,
                        x.Date,
                        x.UnitName,
                        x.Quantity,
                        x.Remarks))
                    .ToList());

        var years = aggregates
            .GroupBy(x => x.Year)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var sources = group
                    .OrderBy(x => x.Source)
                    .Select(row =>
                    {
                        var key = new SourceYearKey(row.Source, row.Year);
                        annualRemarksLookup.TryGetValue(key, out var remarks);
                        detailedLookup.TryGetValue(key, out var entries);

                        return new ProliferationProjectSourceYearViewModel(
                            row.Source,
                            row.SourceLabel,
                            row.AnnualQuantity,
                            row.DetailedQuantity,
                            row.DetailedEntryCount,
                            row.ReportedTotal,
                            row.EffectiveMode,
                            row.CalculationLabel,
                            row.HasCountingException,
                            remarks,
                            row.LastUpdatedOnUtc,
                            entries ?? Array.Empty<ProliferationProjectDetailedEntryViewModel>());
                    })
                    .ToList();

                var sdd = sources
                    .Where(x => x.Source == ProliferationSource.Sdd)
                    .Sum(x => x.ReportedTotal);
                var abw = sources
                    .Where(x => x.Source == ProliferationSource.Abw515)
                    .Sum(x => x.ReportedTotal);

                return new ProliferationProjectYearViewModel(
                    group.Key,
                    new ProliferationSummarySourceTotals(checked(sdd + abw), sdd, abw),
                    sources);
            })
            .ToList();

        var totalSdd = years.Sum(x => x.Totals.Sdd);
        var totalAbw = years.Sum(x => x.Totals.Abw515);

        return new ProliferationProjectDetailViewModel(
            project.Id,
            project.Name,
            project.CaseFileNumber,
            project.TechnicalCategoryName,
            new ProliferationSummarySourceTotals(
                checked(totalSdd + totalAbw),
                totalSdd,
                totalAbw),
            years);
    }

    private readonly record struct SourceYearKey(ProliferationSource Source, int Year);

    private sealed record DetailedEntryProjection(
        Guid Id,
        ProliferationSource Source,
        int Year,
        DateOnly Date,
        string UnitName,
        int Quantity,
        string? Remarks);
}
