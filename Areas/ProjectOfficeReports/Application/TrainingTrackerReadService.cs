using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using DomainTraining = ProjectManagement.Areas.ProjectOfficeReports.Domain.Training;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class TrainingTrackerReadService
{
    private readonly ApplicationDbContext _db;
    private static readonly Guid SimulatorTrainingTypeId = new("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91");

    public TrainingTrackerReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<TrainingTypeOption>> GetTrainingTypesAsync(CancellationToken cancellationToken)
    {
        var types = await _db.TrainingTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new TrainingTypeOption(x.Id, x.Name, x.Id == SimulatorTrainingTypeId))
            .ToListAsync(cancellationToken);

        return types;
    }

    public async Task<IReadOnlyList<ProjectOption>> GetProjectOptionsAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived)
            .OrderBy(x => x.Name)
            .Select(x => new ProjectOption(x.Id, x.Name))
            .ToListAsync(cancellationToken);

        return projects;
    }

    public async Task<TrainingEditorData?> GetEditorAsync(Guid id, CancellationToken cancellationToken)
    {
        var projection = await _db.Trainings
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TrainingEditorData(
                x.Id,
                x.TrainingTypeId,
                x.StartDate,
                x.EndDate,
                x.TrainingMonth,
                x.TrainingYear,
                x.LegacyOfficerCount,
                x.LegacyJcoCount,
                x.LegacyOrCount,
                x.Notes,
                x.ProjectLinks.Select(link => link.ProjectId).ToList(),
                x.Counters != null ? x.Counters.Officers : x.LegacyOfficerCount,
                x.Counters != null ? x.Counters.JuniorCommissionedOfficers : x.LegacyJcoCount,
                x.Counters != null ? x.Counters.OtherRanks : x.LegacyOrCount,
                x.Counters != null ? x.Counters.Total : x.LegacyOfficerCount + x.LegacyJcoCount + x.LegacyOrCount,
                x.Counters != null ? x.Counters.Source : TrainingCounterSource.Legacy,
                x.Trainees.Any(),
                x.Trainees
                    .OrderBy(t => t.Name)
                    .ThenBy(t => t.Id)
                    .Select(t => new TrainingRosterRow
                    {
                        Id = t.Id,
                        ArmyNumber = t.ArmyNumber,
                        Rank = t.Rank,
                        Name = t.Name,
                        UnitName = t.UnitName,
                        Category = t.Category
                    })
                    .ToList(),
                x.DeleteRequests
                    .Where(request => request.Status == TrainingDeleteRequestStatus.Pending)
                    .OrderByDescending(request => request.RequestedAtUtc)
                    .Select(request => new TrainingDeleteRequestSummary(
                        request.Id,
                        request.TrainingId,
                        request.RequestedByUserId,
                        request.RequestedAtUtc,
                        request.Reason,
                        request.Status,
                        request.DecidedByUserId,
                        request.DecidedAtUtc,
                        request.DecisionNotes))
                    .FirstOrDefault(),
                x.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);

        return projection;
    }

    public async Task<IReadOnlyList<TrainingListItem>> SearchAsync(TrainingTrackerQuery? query, CancellationToken cancellationToken)
    {
        var trainings = BuildFilteredQuery(query);

        var projections = await trainings
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.TrainingType!.Name)
            .Select(x => new TrainingListProjection(
                x.Id,
                x.TrainingTypeId,
                x.TrainingType!.Name,
                x.StartDate,
                x.EndDate,
                x.TrainingMonth,
                x.TrainingYear,
                x.LegacyOfficerCount,
                x.LegacyJcoCount,
                x.LegacyOrCount,
                x.Counters != null ? x.Counters.Officers : (int?)null,
                x.Counters != null ? x.Counters.JuniorCommissionedOfficers : (int?)null,
                x.Counters != null ? x.Counters.OtherRanks : (int?)null,
                x.Counters != null ? x.Counters.Total : (int?)null,
                x.Counters != null ? x.Counters.Source : (TrainingCounterSource?)null,
                x.Notes,
                x.RowVersion,
                x.ProjectLinks
                    .OrderBy(link => link.Project != null ? link.Project.Name : string.Empty)
                    .Select(link => new TrainingProjectSnapshot(link.ProjectId, link.Project != null ? link.Project.Name : string.Empty))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var searchQuery = query ?? TrainingTrackerQuery.Empty;

        var filtered = projections
            .Where(item => MatchesDate(item, searchQuery.From, searchQuery.To))
            .Where(item => MatchesCategory(item, searchQuery.Category))
            .Select(item => ToListItem(item))
            .ToList();

        return filtered;
    }

    public async Task<TrainingKpiDto> GetKpisAsync(TrainingTrackerQuery? query, CancellationToken cancellationToken)
    {
        var trainings = BuildFilteredQuery(query);

        var projections = await trainings
            .Select(x => new TrainingListProjection(
                x.Id,
                x.TrainingTypeId,
                x.TrainingType!.Name,
                x.StartDate,
                x.EndDate,
                x.TrainingMonth,
                x.TrainingYear,
                x.LegacyOfficerCount,
                x.LegacyJcoCount,
                x.LegacyOrCount,
                x.Counters != null ? x.Counters.Officers : (int?)null,
                x.Counters != null ? x.Counters.JuniorCommissionedOfficers : (int?)null,
                x.Counters != null ? x.Counters.OtherRanks : (int?)null,
                x.Counters != null ? x.Counters.Total : (int?)null,
                x.Counters != null ? x.Counters.Source : (TrainingCounterSource?)null,
                x.Notes,
                x.RowVersion,
                x.ProjectLinks
                    .OrderBy(link => link.Project != null ? link.Project.Name : string.Empty)
                    .Select(link => new TrainingProjectSnapshot(link.ProjectId, link.Project != null ? link.Project.Name : string.Empty))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var filtered = projections
            .Where(item => MatchesDate(item, query?.From, query?.To))
            .Where(item => MatchesCategory(item, query?.Category))
            .Select(ToListItem)
            .ToList();

        var totalTrainings = filtered.Count;
        var totalTrainees = filtered.Sum(item => item.CounterTotal);

        var byType = filtered
            .GroupBy(item => new { item.TrainingTypeId, item.TrainingTypeName })
            .Select(group => new TrainingTypeKpi
            {
                TypeId = group.Key.TrainingTypeId,
                TypeName = group.Key.TrainingTypeName,
                Trainings = group.Count(),
                Trainees = group.Sum(item => item.CounterTotal)
            })
            .OrderByDescending(entry => entry.Trainees)
            .ThenBy(entry => entry.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trainingIds = filtered.Select(item => item.Id).ToArray();

        var byTechnicalCategory = trainingIds.Length == 0
            ? Array.Empty<TechnicalCategoryKpi>()
            : await BuildTechnicalCategoryKpisAsync(trainingIds, cancellationToken);

        return new TrainingKpiDto
        {
            TotalTrainings = totalTrainings,
            TotalTrainees = totalTrainees,
            ByType = byType,
            ByTechnicalCategory = byTechnicalCategory
        };
    }

    public async Task<IReadOnlyList<TrainingDeleteRequestDto>> GetPendingDeleteRequestsAsync(
        CancellationToken cancellationToken)
    {
        var requests = await _db.TrainingDeleteRequests
            .AsNoTracking()
            .Where(request => request.Status == TrainingDeleteRequestStatus.Pending)
            .OrderByDescending(request => request.RequestedAtUtc)
            .Select(request => new TrainingDeleteRequestProjection(
                request.Id,
                request.TrainingId,
                request.Training!.TrainingType!.Name,
                request.Training.StartDate,
                request.Training.EndDate,
                request.Training.TrainingMonth,
                request.Training.TrainingYear,
                request.Training.Counters != null ? (int?)request.Training.Counters.Total : null,
                request.Training.LegacyOfficerCount,
                request.Training.LegacyJcoCount,
                request.Training.LegacyOrCount,
                request.RequestedByUserId,
                request.RequestedAtUtc,
                request.Reason))
            .ToListAsync(cancellationToken);

        if (requests.Count == 0)
        {
            return Array.Empty<TrainingDeleteRequestDto>();
        }

        var requestedByIds = requests
            .Select(request => request.RequestedByUserId)
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var userDisplayNames = requestedByIds.Length == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (await _db.Users
                .AsNoTracking()
                .Where(user => requestedByIds.Contains(user.Id))
                .Select(user => new { user.Id, user.FullName, user.UserName })
                .ToListAsync(cancellationToken))
                .ToDictionary(
                    user => user.Id,
                    user => string.IsNullOrWhiteSpace(user.FullName)
                        ? (string.IsNullOrWhiteSpace(user.UserName) ? user.Id : user.UserName!)
                        : user.FullName,
                    StringComparer.OrdinalIgnoreCase);

        var result = requests
            .Select(request =>
            {
                var total = request.CounterTotal ?? (request.LegacyOfficerCount + request.LegacyJcoCount + request.LegacyOrCount);
                var period = FormatPeriod(
                    request.StartDate,
                    request.EndDate,
                    request.TrainingMonth,
                    request.TrainingYear);
                var displayName = userDisplayNames.TryGetValue(request.RequestedByUserId, out var name)
                    ? name
                    : request.RequestedByUserId;

                return new TrainingDeleteRequestDto
                {
                    Id = request.Id,
                    TrainingId = request.TrainingId,
                    TrainingTypeName = request.TrainingTypeName,
                    Period = period,
                    TotalTrainees = total,
                    RequestedByUserId = request.RequestedByUserId,
                    RequestedByDisplayName = displayName,
                    RequestedAtUtc = request.RequestedAtUtc,
                    Reason = request.Reason
                };
            })
            .ToList();

        return result;
    }

    public async Task<IReadOnlyList<TrainingExportRow>> ExportAsync(TrainingTrackerQuery? query, CancellationToken cancellationToken)
    {
        var rows = await SearchAsync(query, cancellationToken);

        return rows
            .Select(item => new TrainingExportRow(
                item.Id,
                item.TrainingTypeName,
                FormatPeriod(item),
                item.CounterOfficers,
                item.CounterJcos,
                item.CounterOrs,
                item.CounterTotal,
                item.CounterSource,
                item.ProjectNames,
                item.Notes))
            .ToList();
    }

    private async Task<IReadOnlyList<TechnicalCategoryKpi>> BuildTechnicalCategoryKpisAsync(
        Guid[] trainingIds,
        CancellationToken cancellationToken)
    {
        var perTrainingCategory = await _db.TrainingProjects
            .AsNoTracking()
            .Where(link => trainingIds.Contains(link.TrainingId))
            .Where(link => link.Project != null && link.Project.TechnicalCategoryId.HasValue)
            .Select(link => new
            {
                link.TrainingId,
                CategoryId = link.Project!.TechnicalCategoryId!.Value,
                CategoryName = link.Project.TechnicalCategory!.Name,
                CounterTotal = link.Training!.Counters != null
                    ? (int?)link.Training.Counters.Total
                    : null,
                link.Training.LegacyOfficerCount,
                link.Training.LegacyJcoCount,
                link.Training.LegacyOrCount
            })
            .ToListAsync(cancellationToken);

        if (perTrainingCategory.Count == 0)
        {
            return Array.Empty<TechnicalCategoryKpi>();
        }

        var byTrainingAndCategory = perTrainingCategory
            .GroupBy(entry => new { entry.TrainingId, entry.CategoryId, entry.CategoryName })
            .Select(group => new
            {
                group.Key.CategoryId,
                group.Key.CategoryName,
                Total = group
                    .Select(item => item.CounterTotal ?? (item.LegacyOfficerCount + item.LegacyJcoCount + item.LegacyOrCount))
                    .First()
            })
            .ToList();

        var aggregated = byTrainingAndCategory
            .GroupBy(entry => new { entry.CategoryId, entry.CategoryName })
            .Select(group => new TechnicalCategoryKpi
            {
                TechnicalCategoryId = group.Key.CategoryId,
                TechnicalCategoryName = group.Key.CategoryName,
                Trainings = group.Count(),
                Trainees = group.Sum(item => item.Total)
            })
            .OrderByDescending(entry => entry.Trainees)
            .ThenBy(entry => entry.TechnicalCategoryName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return aggregated;
    }

    private static TrainingListItem ToListItem(TrainingListProjection projection)
    {
        var officers = projection.CounterOfficers ?? projection.LegacyOfficerCount;
        var jcos = projection.CounterJcos ?? projection.LegacyJcoCount;
        var ors = projection.CounterOrs ?? projection.LegacyOrCount;
        var total = projection.CounterTotal ?? (officers + jcos + ors);
        var source = projection.CounterSource ?? TrainingCounterSource.Legacy;

        return new TrainingListItem(
            projection.Id,
            projection.TrainingTypeId,
            projection.TrainingTypeName,
            projection.StartDate,
            projection.EndDate,
            projection.TrainingMonth,
            projection.TrainingYear,
            officers,
            jcos,
            ors,
            total,
            source,
            projection.Notes,
            projection.Projects,
            projection.RowVersion);
    }

    private IQueryable<DomainTraining> BuildFilteredQuery(TrainingTrackerQuery? query)
    {
        query ??= TrainingTrackerQuery.Empty;

        var trainings = _db.Trainings.AsNoTracking();

        if (query.TrainingTypeIds.Count > 0)
        {
            trainings = trainings.Where(x => query.TrainingTypeIds.Contains(x.TrainingTypeId));
        }

        if (query.ProjectId.HasValue)
        {
            var projectId = query.ProjectId.Value;
            trainings = trainings.Where(x => x.ProjectLinks.Any(link => link.ProjectId == projectId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            if (text.Length > 0)
            {
                if (_db.Database.IsNpgsql())
                {
                    trainings = trainings.Where(x =>
                        EF.Functions.ILike(x.Notes ?? string.Empty, $"%{text}%") ||
                        EF.Functions.ILike(x.TrainingType!.Name, $"%{text}%") ||
                        x.ProjectLinks.Any(link => link.Project != null && EF.Functions.ILike(link.Project.Name, $"%{text}%")));
                }
                else
                {
                    trainings = trainings.Where(x =>
                        (x.Notes != null && x.Notes.Contains(text)) ||
                        x.TrainingType!.Name.Contains(text) ||
                        x.ProjectLinks.Any(link => link.Project != null && link.Project.Name.Contains(text)));
                }
            }
        }

        return trainings;
    }

    private static bool MatchesDate(TrainingListProjection projection, DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue && !to.HasValue)
        {
            return true;
        }

        var range = GetDateRange(projection);

        if (from.HasValue && range.End.HasValue && range.End.Value < from.Value)
        {
            return false;
        }

        if (to.HasValue && range.Start.HasValue && range.Start.Value > to.Value)
        {
            return false;
        }

        if (from.HasValue && !range.End.HasValue)
        {
            return false;
        }

        if (to.HasValue && !range.Start.HasValue)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesCategory(TrainingListProjection projection, TrainingCategory? category)
    {
        if (!category.HasValue)
        {
            return true;
        }

        var officers = projection.CounterOfficers ?? projection.LegacyOfficerCount;
        var jcos = projection.CounterJcos ?? projection.LegacyJcoCount;
        var ors = projection.CounterOrs ?? projection.LegacyOrCount;

        return category.Value switch
        {
            TrainingCategory.Officer => officers > 0,
            TrainingCategory.JuniorCommissionedOfficer => jcos > 0,
            TrainingCategory.OtherRank => ors > 0,
            _ => true
        };
    }

    private static TrainingDateRange GetDateRange(TrainingListProjection projection)
    {
        if (projection.StartDate.HasValue || projection.EndDate.HasValue)
        {
            return new TrainingDateRange(projection.StartDate, projection.EndDate ?? projection.StartDate);
        }

        if (projection.TrainingYear.HasValue && projection.TrainingMonth.HasValue
            && projection.TrainingMonth.Value is >= 1 and <= 12
            && projection.TrainingYear.Value is >= 1 and <= 9999)
        {
            var start = new DateOnly(projection.TrainingYear.Value, projection.TrainingMonth.Value, 1);
            var end = start.AddMonths(1).AddDays(-1);
            return new TrainingDateRange(start, end);
        }

        return new TrainingDateRange(null, null);
    }

    private static string FormatPeriod(TrainingListItem item)
        => FormatPeriod(item.StartDate, item.EndDate, item.TrainingMonth, item.TrainingYear);

    private static string FormatPeriod(DateOnly? startDate, DateOnly? endDate, int? trainingMonth, int? trainingYear)
    {
        if (startDate.HasValue || endDate.HasValue)
        {
            var start = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(not set)";
            var end = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? start;
            return start == end ? start : $"{start} – {end}";
        }

        if (trainingYear.HasValue && trainingMonth.HasValue)
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(trainingMonth.Value);
            return $"{monthName} {trainingYear.Value}";
        }

        return "(unspecified)";
    }

    private sealed record TrainingDeleteRequestProjection(
        Guid Id,
        Guid TrainingId,
        string TrainingTypeName,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? TrainingMonth,
        int? TrainingYear,
        int? CounterTotal,
        int LegacyOfficerCount,
        int LegacyJcoCount,
        int LegacyOrCount,
        string RequestedByUserId,
        DateTimeOffset RequestedAtUtc,
        string Reason);

    private sealed record TrainingListProjection(
        Guid Id,
        Guid TrainingTypeId,
        string TrainingTypeName,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? TrainingMonth,
        int? TrainingYear,
        int LegacyOfficerCount,
        int LegacyJcoCount,
        int LegacyOrCount,
        int? CounterOfficers,
        int? CounterJcos,
        int? CounterOrs,
        int? CounterTotal,
        TrainingCounterSource? CounterSource,
        string? Notes,
        byte[] RowVersion,
        IReadOnlyList<TrainingProjectSnapshot> Projects);

    private readonly record struct TrainingDateRange(DateOnly? Start, DateOnly? End);
}

public sealed record TrainingTypeOption(Guid Id, string Name, bool RequiresProjectSelection);

public sealed record ProjectOption(int Id, string Name);

public sealed record TrainingProjectSnapshot(int ProjectId, string Name);

public sealed record TrainingEditorData(
    Guid Id,
    Guid TrainingTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? TrainingMonth,
    int? TrainingYear,
    int LegacyOfficerCount,
    int LegacyJcoCount,
    int LegacyOrCount,
    string? Notes,
    IReadOnlyList<int> ProjectIds,
    int CounterOfficers,
    int CounterJcos,
    int CounterOrs,
    int CounterTotal,
    TrainingCounterSource CounterSource,
    bool HasRoster,
    IReadOnlyList<TrainingRosterRow> Roster,
    TrainingDeleteRequestSummary? PendingDeleteRequest,
    byte[] RowVersion);

public sealed record TrainingDeleteRequestSummary(
    Guid Id,
    Guid TrainingId,
    string RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    string Reason,
    TrainingDeleteRequestStatus Status,
    string? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNotes);

public sealed record TrainingListItem(
    Guid Id,
    Guid TrainingTypeId,
    string TrainingTypeName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? TrainingMonth,
    int? TrainingYear,
    int CounterOfficers,
    int CounterJcos,
    int CounterOrs,
    int CounterTotal,
    TrainingCounterSource CounterSource,
    string? Notes,
    IReadOnlyList<TrainingProjectSnapshot> Projects,
    byte[] RowVersion)
{
    public IReadOnlyList<string> ProjectNames => Projects.Select(project => project.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
};

public sealed record TrainingExportRow(
    Guid Id,
    string TrainingTypeName,
    string Period,
    int Officers,
    int JuniorCommissionedOfficers,
    int OtherRanks,
    int Total,
    TrainingCounterSource Source,
    IReadOnlyList<string> Projects,
    string? Notes);

public sealed class TrainingTrackerQuery
{
    public static TrainingTrackerQuery Empty { get; } = new();

    public IList<Guid> TrainingTypeIds { get; } = new List<Guid>();

    public int? ProjectId { get; set; }

    public TrainingCategory? Category { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public string? Search { get; set; }
}
