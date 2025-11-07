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

namespace ProjectManagement.Areas.ProjectOfficeReports.Application
{
    // ============================================================
    // TRAINING TRACKER READ SERVICE
    // ============================================================
    public sealed class TrainingTrackerReadService
    {
        // ------------------------------------------------------------
        // constants / ctor
        // ------------------------------------------------------------
        private readonly ApplicationDbContext _db;
        private static readonly Guid SimulatorTrainingTypeId = new("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91");
        private static readonly Guid DroneTrainingTypeId = new("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b");

        public TrainingTrackerReadService(ApplicationDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // ============================================================
        // LOOKUP / OPTIONS
        // ============================================================
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

        public async Task<IReadOnlyList<ProjectTechnicalCategoryOption>> GetProjectTechnicalCategoryOptionsAsync(
            CancellationToken cancellationToken)
        {
            var categories = await _db.TechnicalCategories
                .AsNoTracking()
                .Select(category => new ProjectTechnicalCategoryOption(
                    category.Id,
                    category.Name,
                    category.ParentId,
                    category.IsActive))
                .ToListAsync(cancellationToken);

            return categories;
        }

        // ============================================================
        // EDITOR LOAD
        // ============================================================
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

            if (projection?.PendingDeleteRequest is { } pending)
            {
                var requestedBy = await _db.Users
                    .AsNoTracking()
                    .Where(user => user.Id == pending.RequestedByUserId)
                    .Select(user => new
                    {
                        user.FullName,
                        user.UserName,
                        user.Email
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                var requestedByDisplayName = requestedBy is not null
                    ? !string.IsNullOrWhiteSpace(requestedBy.FullName)
                        ? requestedBy.FullName
                        : !string.IsNullOrWhiteSpace(requestedBy.UserName)
                            ? requestedBy.UserName!
                            : !string.IsNullOrWhiteSpace(requestedBy.Email)
                                ? requestedBy.Email!
                                : pending.RequestedByUserId
                    : pending.RequestedByUserId;

                projection = projection with
                {
                    PendingDeleteRequest = pending with { RequestedByDisplayName = requestedByDisplayName }
                };
            }

            return projection;
        }

        // ============================================================
        // MAIN LIST SEARCH
        // ============================================================
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

        // ============================================================
        // PAGED LIST SEARCH (for the dedicated records page)
        // ============================================================
        public async Task<PagedResult<TrainingListItem>> SearchPagedAsync(
            TrainingTrackerQuery? query,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (pageNumber < 1)
            {
                pageNumber = 1;
            }

            if (pageSize < 1)
            {
                pageSize = 20;
            }

            // reuse existing logic so filters/date/category stay in ONE place
            var all = await SearchAsync(query, cancellationToken);

            // your SearchAsync already gives you filtered rows,
            // but not ordered by recency consistently, so let’s do that here
            var ordered = all
                .OrderByDescending(r => r.StartDate ?? r.EndDate)  // recent first
                .ThenByDescending(r => r.CounterTotal)
                .ToList();

            var total = ordered.Count;
            var skip = (pageNumber - 1) * pageSize;
            var items = ordered
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new PagedResult<TrainingListItem>(items, total, pageNumber, pageSize);
        }

        // ============================================================
        // KPI BUILD
        // ============================================================
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

            // top-level totals
            var totalTrainings = filtered.Count;
            var totalTrainees = filtered.Sum(item => item.CounterTotal);

            // KPI by type (Simulator / Drone / etc.) — we keep using defaults for strength
            var byType = filtered
                .GroupBy(item => new { item.TrainingTypeId, item.TrainingTypeName })
                .Select(group => new TrainingKpiByTypeDto(
                    group.Key.TrainingTypeId,
                    group.Key.TrainingTypeName,
                    group.Count(),
                    group.Sum(item => item.CounterTotal)))
                .OrderByDescending(entry => entry.Trainees)
                .ThenBy(entry => entry.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // technical category KPIs (this is what powers the cards you circled)
            var trainingIds = filtered.Select(item => item.Id).ToArray();

            var byTechnicalCategory = trainingIds.Length == 0
                ? Array.Empty<TrainingKpiByTechnicalCategoryDto>()
                : await BuildTechnicalCategoryKpisAsync(trainingIds, cancellationToken);

            // training year buckets for chart — now with trainee counts per simulator/drone
            var byTrainingYear = BuildTrainingYearBuckets(filtered);

            return new TrainingKpiDto
            {
                TotalTrainings = totalTrainings,
                TotalTrainees = totalTrainees,
                ByType = byType,
                ByTechnicalCategory = byTechnicalCategory,
                ByTrainingYear = byTrainingYear
            };
        }

        // ============================================================
        // PENDING DELETE REQUESTS
        // ============================================================
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

        // ============================================================
        // EXPORT
        // ============================================================
        public async Task<IReadOnlyList<TrainingExportDetail>> ExportAsync(
            TrainingTrackerQuery? query,
            bool includeRoster,
            CancellationToken cancellationToken)
        {
            var rows = await SearchAsync(query, cancellationToken);

            var summaries = rows
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

            if (!includeRoster || summaries.Count == 0)
            {
                return summaries
                    .Select(summary => new TrainingExportDetail(summary, Array.Empty<TrainingRosterRow>()))
                    .ToList();
            }

            var rosterLookup = await LoadRosterAsync(rows.Select(item => item.Id).ToArray(), cancellationToken);

            return summaries
                .Select(summary =>
                {
                    var roster = rosterLookup.TryGetValue(summary.Id, out var trainees)
                        ? trainees
                        : Array.Empty<TrainingRosterRow>();

                    return new TrainingExportDetail(summary, roster);
                })
                .ToList();
        }

        // ============================================================
        // HELPER: load roster
        // ============================================================
        private async Task<Dictionary<Guid, IReadOnlyList<TrainingRosterRow>>> LoadRosterAsync(
            Guid[] trainingIds,
            CancellationToken cancellationToken)
        {
            if (trainingIds.Length == 0)
            {
                return new Dictionary<Guid, IReadOnlyList<TrainingRosterRow>>();
            }

            var rosterRows = await _db.TrainingTrainees
                .AsNoTracking()
                .Where(trainee => trainingIds.Contains(trainee.TrainingId))
                .OrderBy(trainee => trainee.TrainingId)
                .ThenBy(trainee => trainee.Category)
                .ThenBy(trainee => trainee.Rank)
                .ThenBy(trainee => trainee.Name)
                .ThenBy(trainee => trainee.Id)
                .Select(trainee => new
                {
                    trainee.TrainingId,
                    Row = new TrainingRosterRow
                    {
                        Id = trainee.Id,
                        ArmyNumber = trainee.ArmyNumber,
                        Rank = trainee.Rank,
                        Name = trainee.Name,
                        UnitName = trainee.UnitName,
                        Category = trainee.Category
                    }
                })
                .ToListAsync(cancellationToken);

            return rosterRows
                .GroupBy(entry => entry.TrainingId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<TrainingRosterRow>)group.Select(entry => entry.Row).ToList());
        }

        // ============================================================
        // FIXED: TECHNICAL CATEGORY KPIs WITH STRENGTH
        // ============================================================
        private async Task<IReadOnlyList<TrainingKpiByTechnicalCategoryDto>> BuildTechnicalCategoryKpisAsync(
            Guid[] trainingIds,
            CancellationToken cancellationToken)
        {
            // load links + the strength on the linked training
            var perTrainingCategory = await _db.TrainingProjects
                .AsNoTracking()
                .Where(link => trainingIds.Contains(link.TrainingId))
                .Where(link => link.Project != null && link.Project.TechnicalCategoryId.HasValue)
                .Select(link => new
                {
                    link.TrainingId,
                    CategoryId = link.Project!.TechnicalCategoryId!.Value,
                    CategoryName = link.Project.TechnicalCategory!.Name,
                    // training strength (either counters or legacy)
                    Counters = link.Training!.Counters,
                    link.Training.LegacyOfficerCount,
                    link.Training.LegacyJcoCount,
                    link.Training.LegacyOrCount
                })
                .ToListAsync(cancellationToken);

            if (perTrainingCategory.Count == 0)
            {
                return Array.Empty<TrainingKpiByTechnicalCategoryDto>();
            }

            // first: normalise each row to 1 training+1 category with real strength
            var normalised = perTrainingCategory
                .Select(entry =>
                {
                    var officers = entry.Counters?.Officers ?? entry.LegacyOfficerCount;
                    var jcos = entry.Counters?.JuniorCommissionedOfficers ?? entry.LegacyJcoCount;
                    var ors = entry.Counters?.OtherRanks ?? entry.LegacyOrCount;
                    var total = entry.Counters?.Total ?? (officers + jcos + ors);

                    return new
                    {
                        entry.CategoryId,
                        entry.CategoryName,
                        Officers = officers,
                        Jcos = jcos,
                        Ors = ors,
                        Total = total
                    };
                })
                .ToList();

            // now group by category and aggregate
            var aggregated = normalised
                .GroupBy(x => new { x.CategoryId, x.CategoryName })
                .Select(g => new TrainingKpiByTechnicalCategoryDto(
                    g.Key.CategoryId,
                    g.Key.CategoryName,
                    Trainings: g.Count(),                    // # of trainings in that category
                    Trainees: g.Sum(x => x.Total),          // total people
                    Officers: g.Sum(x => x.Officers),
                    Jcos: g.Sum(x => x.Jcos),
                    Ors: g.Sum(x => x.Ors)
                ))
                .OrderByDescending(x => x.Trainees)
                .ThenBy(x => x.TechnicalCategoryName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return aggregated;
        }

        // ============================================================
        // FIXED: TRAINING YEAR BUCKETS WITH TRAINEE COUNTS PER TYPE
        // ============================================================
        private static IReadOnlyList<TrainingYearBucketDto> BuildTrainingYearBuckets(IEnumerable<TrainingListItem> trainings)
        {
            var candidates = new List<(TrainingListItem Item, int StartYear)>();

            foreach (var training in trainings)
            {
                if (TryGetTrainingYearStart(training, out var startYear))
                {
                    candidates.Add((training, startYear));
                }
            }

            if (candidates.Count == 0)
            {
                return Array.Empty<TrainingYearBucketDto>();
            }

            var buckets = candidates
                .GroupBy(entry => entry.StartYear)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    // trainee counts per type (not just count of trainings)
                    var simulatorTrainees = group
                        .Where(entry => entry.Item.TrainingTypeId == SimulatorTrainingTypeId)
                        .Sum(entry => entry.Item.CounterTotal);

                    var droneTrainees = group
                        .Where(entry => entry.Item.TrainingTypeId == DroneTrainingTypeId)
                        .Sum(entry => entry.Item.CounterTotal);

                    var totalTrainings = group.Count();
                    var totalTrainees = group.Sum(entry => entry.Item.CounterTotal);

                    return new TrainingYearBucketDto(
                        TrainingYearLabel: FormatTrainingYearLabel(group.Key),
                        SimulatorTrainings: simulatorTrainees, // JS will read this as trainees now
                        DroneTrainings: droneTrainees,
                        TotalTrainings: totalTrainings,
                        TotalTrainees: totalTrainees);
                })
                .ToList();

            return buckets;
        }

        // ============================================================
        // QUERY BUILD + FILTERS
        // ============================================================
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

            if (query.ProjectTechnicalCategoryId.HasValue)
            {
                var technicalCategoryId = query.ProjectTechnicalCategoryId.Value;
                trainings = trainings.Where(x => x.ProjectLinks.Any(link =>
                    link.Project != null && link.Project.TechnicalCategoryId == technicalCategoryId));
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

        // ============================================================
        // MATCH HELPERS
        // ============================================================
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
                var start = projection.StartDate ?? projection.EndDate;
                var end = projection.EndDate ?? projection.StartDate ?? start;
                return new TrainingDateRange(start, end);
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

        // ============================================================
        // MAPPERS / FORMATTERS
        // ============================================================
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

        private static bool TryGetTrainingYearStart(TrainingListItem item, out int startYear)
        {
            if (item.StartDate.HasValue)
            {
                startYear = GetTrainingYearStartYear(item.StartDate.Value);
                return true;
            }

            if (item.EndDate.HasValue)
            {
                startYear = GetTrainingYearStartYear(item.EndDate.Value);
                return true;
            }

            if (item.TrainingYear.HasValue && item.TrainingMonth.HasValue
                && item.TrainingMonth.Value is >= 1 and <= 12
                && item.TrainingYear.Value is >= 1 and <= 9999)
            {
                var reference = new DateOnly(item.TrainingYear.Value, item.TrainingMonth.Value, 1);
                startYear = GetTrainingYearStartYear(reference);
                return true;
            }

            startYear = default;
            return false;
        }

        private static int GetTrainingYearStartYear(DateOnly date)
            => date.Month >= 4 ? date.Year : date.Year - 1;

        private static string FormatTrainingYearLabel(int startYear)
        {
            var endYear = startYear + 1;
            return $"{startYear}-{endYear % 100:00}";
        }

        private static string FormatPeriod(TrainingListItem item)
            => FormatPeriod(item.StartDate, item.EndDate, item.TrainingMonth, item.TrainingYear);

        private static string FormatPeriod(DateOnly? startDate, DateOnly? endDate, int? trainingMonth, int? trainingYear)
        {
            if (startDate.HasValue || endDate.HasValue)
            {
                var normalizedStart = startDate ?? endDate;
                var normalizedEnd = endDate ?? startDate ?? normalizedStart;
                var start = normalizedStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(not set)";
                var end = normalizedEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? start;
                var period = start == end ? start : $"{start} – {end}";

                if (startDate.HasValue && normalizedEnd.HasValue)
                {
                    var dayCount = normalizedEnd.Value.DayNumber - startDate.Value.DayNumber + 1;
                    var dayCountText = FormatDayCount(dayCount);
                    return string.Concat(period, " (", dayCountText, ")");
                }

                return period;
            }

            if (trainingYear.HasValue && trainingMonth.HasValue)
            {
                var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(trainingMonth.Value);
                return $"{monthName} {trainingYear.Value}";
            }

            return "(unspecified)";
        }

        private static string FormatDayCount(int dayCount)
        {
            if (dayCount <= 1)
            {
                return "1 day";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} days",
                dayCount);
        }

        // ============================================================
        // INTERNAL RECORDS
        // ============================================================
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

        // ============================================================
        // SHARED PAGED RESULT
        // ============================================================
        public sealed record PagedResult<T>(
            IReadOnlyList<T> Items,
            int TotalCount,
            int PageNumber,
            int PageSize);

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

    // ============================================================
    // SUPPORTING RECORDS (same as your file)
    // ============================================================
    public sealed record TrainingTypeOption(Guid Id, string Name, bool RequiresProjectSelection);

    public sealed record ProjectOption(int Id, string Name);

    public sealed record ProjectTechnicalCategoryOption(int Id, string Name, int? ParentId, bool IsActive);

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
        string RequestedByDisplayName,
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

    public sealed record TrainingExportDetail(TrainingExportRow Summary, IReadOnlyList<TrainingRosterRow> Roster);

    // ============================================================
    // QUERY DTO
    // ============================================================
    public sealed class TrainingTrackerQuery
    {
        public static TrainingTrackerQuery Empty { get; } = new();

        public IList<Guid> TrainingTypeIds { get; } = new List<Guid>();

        public int? ProjectId { get; set; }

        public int? ProjectTechnicalCategoryId { get; set; }

        public TrainingCategory? Category { get; set; }

        public DateOnly? From { get; set; }

        public DateOnly? To { get; set; }

        public string? Search { get; set; }
    }
}
