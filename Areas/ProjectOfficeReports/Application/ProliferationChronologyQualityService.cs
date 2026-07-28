using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// Describes approved proliferation records whose year falls outside the supported
/// chronological range. The reported quantity is calculated from the same aggregate
/// reader used by the overview and reports so the disclosure reflects the authoritative
/// counting preference rather than a raw sum of source records.
/// </summary>
public sealed class ProliferationChronologyQualityService
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationAggregateReadService _aggregateReadService;
    private readonly IClock _clock;

    public ProliferationChronologyQualityService(
        ApplicationDbContext db,
        ProliferationAggregateReadService aggregateReadService,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProliferationChronologyQualitySummary> GetApprovedSummaryAsync(
        IReadOnlyCollection<int>? projectIds,
        ProliferationSource? source,
        CancellationToken cancellationToken)
    {
        var minimumYear = ProliferationYearPolicy.MinimumYear;
        var maximumYear = ProliferationYearPolicy.GetMaximumYear(_clock.UtcNow);
        var projectIdArray = projectIds?
            .Where(id => id > 0)
            .Distinct()
            .ToArray() ?? Array.Empty<int>();

        var aggregates = await _aggregateReadService.GetApprovedAggregatesAsync(
            projectId: null,
            cancellationToken);

        var scopeProjectIds = projectIdArray.Length > 0
            ? projectIdArray
            : aggregates.Select(row => row.ProjectId).Distinct().ToArray();

        if (scopeProjectIds.Length == 0)
        {
            return new ProliferationChronologyQualitySummary(
                0,
                0,
                0,
                minimumYear,
                maximumYear);
        }

        var yearlyQuery = _db.ProliferationYearlies
            .AsNoTracking()
            .Where(row => row.ApprovalStatus == ApprovalStatus.Approved)
            .Where(row => scopeProjectIds.Contains(row.ProjectId))
            .Where(row => row.Year < minimumYear || row.Year > maximumYear);

        var detailedQuery = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(row => row.ApprovalStatus == ApprovalStatus.Approved)
            .Where(row => scopeProjectIds.Contains(row.ProjectId))
            .Where(row => row.ProliferationDate.Year < minimumYear
                          || row.ProliferationDate.Year > maximumYear);

        if (source.HasValue)
        {
            yearlyQuery = yearlyQuery.Where(row => row.Source == source.Value);
            detailedQuery = detailedQuery.Where(row => row.Source == source.Value);
        }

        var yearlyCount = await yearlyQuery.CountAsync(cancellationToken);
        var detailedCount = await detailedQuery.CountAsync(cancellationToken);

        var invalidPositions = aggregates
            .Where(row => row.Year < minimumYear || row.Year > maximumYear)
            .Where(row => scopeProjectIds.Contains(row.ProjectId))
            .Where(row => !source.HasValue || row.Source == source.Value)
            .ToList();

        return new ProliferationChronologyQualitySummary(
            checked(yearlyCount + detailedCount),
            invalidPositions.Count,
            invalidPositions.Sum(row => row.ReportedTotal),
            minimumYear,
            maximumYear);
    }

    public static string BuildDisclosure(
        ProliferationChronologyQualitySummary quality,
        bool allTimeReport)
    {
        ArgumentNullException.ThrowIfNull(quality);

        if (!quality.HasIssues)
        {
            return "No approved records are assigned to years outside the supported chronological range.";
        }

        var recordLabel = quality.ApprovedRecordCount == 1 ? "record" : "records";
        var quantityLabel = quality.ReportedQuantity == 1 ? "reported unit" : "reported units";
        var range = $"{quality.MinimumValidYear}–{quality.MaximumValidYear}";

        if (allTimeReport)
        {
            return $"All-time totals include {quality.ReportedQuantity:N0} {quantityLabel} from "
                   + $"{quality.ApprovedRecordCount:N0} approved {recordLabel} assigned to years outside {range}. "
                   + "These quantities are excluded from year-wise analysis until the source records are corrected.";
        }

        return $"{quality.ApprovedRecordCount:N0} approved {recordLabel} assigned to years outside {range} "
               + $"are excluded from this chronological report. The equivalent all-time position includes "
               + $"{quality.ReportedQuantity:N0} {quantityLabel} from those records.";
    }
}
