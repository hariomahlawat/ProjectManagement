using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Reports.ProgressReview;

/// <summary>
/// Aggregates cross-module data for the Progress Review report.
/// All filters operate on occurrence dates (DateOnly) rather than entry timestamps.
/// </summary>
public sealed class ProgressReviewService : IProgressReviewService
{
    private static readonly TimeOnly EndOfDay = new(23, 59, 59);
    private static readonly Guid SimulatorTrainingTypeId = new("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91");
    private static readonly Guid DroneTrainingTypeId = new("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b");

    private readonly ApplicationDbContext _db;

    public ProgressReviewService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ProgressReviewVm> GetAsync(ProgressReviewRequest request, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(request.From, request.To);

        var stageChangeRows = await LoadStageChangeRowsAsync(from, to, cancellationToken);

        // SECTION: Project stage movement & remarks buckets
        var projectFrontRunners = await LoadFrontRunnerProjectsAsync(stageChangeRows, from, to, cancellationToken);
        var projectsWithMovement = projectFrontRunners.Select(p => p.ProjectId).ToHashSet();

        var projectRemarksOnly = await LoadProjectRemarksOnlyAsync(from, to, projectsWithMovement, cancellationToken);
        foreach (var remarkProjectId in projectRemarksOnly.Select(p => p.ProjectId))
        {
            projectsWithMovement.Add(remarkProjectId);
        }

        var projectNonMovers = await LoadProjectNonMoversAsync(from, to, projectsWithMovement, cancellationToken);

        var summaryProjectIds = new HashSet<int>(projectsWithMovement);
        foreach (var idle in projectNonMovers)
        {
            summaryProjectIds.Add(idle.ProjectId);
        }

        var presentStageLookup = await BuildPresentStageLookupAsync(summaryProjectIds, cancellationToken);
        var remarkLookup = await BuildRemarkSummaryLookupAsync(summaryProjectIds, from, to, cancellationToken);
        var projectSummaryRows = BuildProjectSummaryRows(
            projectFrontRunners,
            projectRemarksOnly,
            projectNonMovers,
            presentStageLookup,
            remarkLookup,
            from,
            to);
        var projectsWithAnyRemarks = remarkLookup
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.LatestRemarkSummary))
            .Select(pair => pair.Key)
            .ToHashSet();

        // SECTION: Visits & social media
        var visits = await LoadVisitsAsync(from, to, cancellationToken);
        var socialMedia = await LoadSocialMediaAsync(from, to, cancellationToken);

        // SECTION: ToT
        var totStage = LoadTotStageChanges(stageChangeRows);
        var totRemarks = await LoadTotRemarksAsync(from, to, cancellationToken);

        // SECTION: IPR
        var iprStatus = await LoadIprStatusChangesAsync(from, to, cancellationToken);
        var iprRemarks = await LoadIprRemarksAsync(from, to, cancellationToken);

        // SECTION: Trainings
        var simulatorTraining = await LoadTrainingBlockAsync(from, to, SimulatorTrainingTypeId, cancellationToken);
        var droneTraining = await LoadTrainingBlockAsync(from, to, DroneTrainingTypeId, cancellationToken);

        // SECTION: Proliferation, FFC, Misc
        var proliferation = await LoadProliferationAsync(from, to, cancellationToken);
        var ffc = await LoadFfcAsync(from, to, cancellationToken);
        var misc = await LoadMiscActivitiesAsync(from, to, cancellationToken);

        var totals = new TotalsVm(
            ProjectsMoved: projectFrontRunners.Count,
            ProjectsWithRemarks: projectsWithAnyRemarks.Count,
            NonMovers: projectNonMovers.Count,
            VisitsCount: visits.TotalCount,
            SocialPostsCount: socialMedia.TotalCount,
            TotChangesCount: totStage.Count + totRemarks.Count,
            IprChangesCount: iprStatus.Count + iprRemarks.Count,
            SimulatorTrainees: simulatorTraining.TotalPersons,
            DroneTrainees: droneTraining.TotalPersons,
            ProliferationsCount: proliferation.Rows.Count,
            FfcItemsChanged: ffc.Rows.Count,
            MiscCount: misc.Rows.Count);

        return new ProgressReviewVm(
            Range: new RangeVm(from, to),
            Projects: new ProjectSectionVm(projectFrontRunners, projectRemarksOnly, projectNonMovers, projectSummaryRows),
            Visits: visits,
            SocialMedia: socialMedia,
            Tot: new TotSectionVm(totStage, totRemarks),
            Ipr: new IprSectionVm(iprStatus, iprRemarks),
            Training: new TrainingSectionVm(simulatorTraining, droneTraining),
            Proliferation: proliferation,
            Ffc: ffc,
            Misc: misc,
            Totals: totals);
    }

    // -----------------------------------------------------------------
    // SECTION: Projects (front runners, remarks only, non-movers)
    // -----------------------------------------------------------------
    private async Task<IReadOnlyList<ProjectStageChangeVm>> LoadFrontRunnerProjectsAsync(
        IReadOnlyList<StageChangeLogRow> stageChanges,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (stageChanges.Count == 0)
        {
            return Array.Empty<ProjectStageChangeVm>();
        }

        return stageChanges
            .OrderByDescending(x => x.ChangeDate)
            .ThenBy(x => x.ProjectName)
            .Select(x => new ProjectStageChangeVm(
                x.ProjectId,
                x.ProjectName,
                x.StageCode,
                StageCodes.DisplayNameOf(x.StageCode),
                x.FromStatus,
                x.ToStatus,
                x.ChangeDate,
                x.ToActualStart,
                x.ToCompletedOn))
            .ToList();
    }

    private async Task<IReadOnlyList<StageChangeLogRow>> LoadStageChangeRowsAsync(
        DateOnly rangeFrom,
        DateOnly rangeTo,
        CancellationToken cancellationToken)
    {
        var fromDateTime = rangeFrom.ToDateTime(TimeOnly.MinValue);
        var toDateTime = rangeTo.ToDateTime(EndOfDay);

        var rows = await (from log in _db.StageChangeLogs.AsNoTracking()
                          join project in _db.Projects.AsNoTracking() on log.ProjectId equals project.Id
                          where log.Action == "Applied"
                          where project.LifecycleStatus == ProjectLifecycleStatus.Active
                                && !project.IsArchived
                                && !project.IsDeleted
                          where
                              (log.ToActualStart.HasValue && log.ToActualStart.Value >= rangeFrom && log.ToActualStart.Value <= rangeTo)
                              || (log.ToCompletedOn.HasValue && log.ToCompletedOn.Value >= rangeFrom && log.ToCompletedOn.Value <= rangeTo)
                              || (log.FromActualStart.HasValue && log.FromActualStart.Value >= rangeFrom && log.FromActualStart.Value <= rangeTo)
                              || (log.FromCompletedOn.HasValue && log.FromCompletedOn.Value >= rangeFrom && log.FromCompletedOn.Value <= rangeTo)
                              || (!log.ToActualStart.HasValue && !log.ToCompletedOn.HasValue
                                  && !log.FromActualStart.HasValue && !log.FromCompletedOn.HasValue
                                  && log.At >= fromDateTime && log.At <= toDateTime)
                          select new StageChangeProjection(
                              log.ProjectId,
                              project.Name,
                              log.StageCode,
                              log.FromStatus,
                              log.ToStatus,
                              log.ToActualStart,
                              log.ToCompletedOn,
                              log.FromActualStart,
                              log.FromCompletedOn,
                              log.At,
                              log.Note))
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new StageChangeLogRow(
                row.ProjectId,
                row.ProjectName,
                row.StageCode,
                row.FromStatus,
                row.ToStatus,
                ResolveChangeDate(row),
                row.ToActualStart,
                row.ToCompletedOn,
                row.Note))
            .ToList();
    }

    private async Task<Dictionary<int, ProjectRemarkSummaryVm>> BuildRemarkSummaryLookupAsync(
        IReadOnlyCollection<int> projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, ProjectRemarkSummaryVm>();
        }

        var remarkRows = await _db.Remarks
            .AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId))
            .Where(r => !r.IsDeleted)
            .Where(r => r.Scope == RemarkScope.General)
            .Where(r => r.EventDate >= from && r.EventDate <= to)
            .Select(r => new
            {
                r.ProjectId,
                r.EventDate,
                r.CreatedAtUtc,
                r.Body,
                r.AuthorRole
            })
            .ToListAsync(cancellationToken);

        return remarkRows
            .GroupBy(r => r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => BuildRemarkSummary(g.Select(x => (x.EventDate, x.CreatedAtUtc, x.Body, x.AuthorRole))));
    }

    private async Task<IReadOnlyList<ProjectRemarkOnlyVm>> LoadProjectRemarksOnlyAsync(
        DateOnly from,
        DateOnly to,
        HashSet<int> excludedProjectIds,
        CancellationToken cancellationToken)
    {
        var remarkRows = await _db.Remarks
            .AsNoTracking()
            .Where(r => r.Scope == RemarkScope.General)
            .Where(r => !r.IsDeleted)
            .Where(r => r.EventDate >= from && r.EventDate <= to)
            .Where(r => r.Project != null && r.Project.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Where(r => r.Project != null && !r.Project.IsArchived && !r.Project.IsDeleted)
            .Where(r => !excludedProjectIds.Contains(r.ProjectId))
            .Select(r => new
            {
                r.ProjectId,
                ProjectName = r.Project!.Name,
                r.EventDate,
                r.CreatedAtUtc,
                r.Body,
                r.AuthorRole
            })
            .ToListAsync(cancellationToken);

        return remarkRows
            .GroupBy(r => new { r.ProjectId, r.ProjectName })
            .Select(g => new ProjectRemarkOnlyVm(
                g.Key.ProjectId,
                g.Key.ProjectName,
                BuildRemarkSummary(g.Select(x => (x.EventDate, x.CreatedAtUtc, x.Body, x.AuthorRole)))))
            .OrderBy(x => x.ProjectName)
            .ToList();
    }

    private static ProjectRemarkSummaryVm BuildRemarkSummary(
        IEnumerable<(DateOnly EventDate, DateTime CreatedAtUtc, string? Body, RemarkActorRole AuthorRole)> remarks)
    {
        var ordered = remarks
            .OrderByDescending(x => x.EventDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToList();

        if (ordered.Count == 0)
        {
            return ProjectRemarkSummaryVm.Empty;
        }

        var latest = ordered[0];
        return new ProjectRemarkSummaryVm(
            latest.EventDate,
            Truncate(latest.Body, 220),
            latest.AuthorRole,
            Math.Max(0, ordered.Count - 1));
    }

    private async Task<IReadOnlyList<ProjectNonMoverVm>> LoadProjectNonMoversAsync(
        DateOnly from,
        DateOnly to,
        HashSet<int> excludedProjectIds,
        CancellationToken cancellationToken)
    {
        var baseProjects = await _db.Projects
            .AsNoTracking()
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Where(p => !p.IsArchived && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        var candidateProjects = baseProjects
            .Where(p => !excludedProjectIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToArray();

        if (candidateProjects.Length == 0)
        {
            return Array.Empty<ProjectNonMoverVm>();
        }

        var stageActivity = await (from log in _db.StageChangeLogs.AsNoTracking()
                                   where log.Action == "Applied"
                                   where candidateProjects.Contains(log.ProjectId)
                                   where
                                       (log.ToActualStart.HasValue && log.ToActualStart.Value <= to)
                                       || (log.ToCompletedOn.HasValue && log.ToCompletedOn.Value <= to)
                                       || (log.FromActualStart.HasValue && log.FromActualStart.Value <= to)
                                       || (log.FromCompletedOn.HasValue && log.FromCompletedOn.Value <= to)
                                       || (!log.ToActualStart.HasValue && !log.ToCompletedOn.HasValue
                                           && !log.FromActualStart.HasValue && !log.FromCompletedOn.HasValue
                                           && log.At <= to.ToDateTime(EndOfDay))
                                   select new StageChangeProjection(
                                       log.ProjectId,
                                       string.Empty,
                                       log.StageCode,
                                       log.FromStatus,
                                       log.ToStatus,
                                       log.ToActualStart,
                                       log.ToCompletedOn,
                                       log.FromActualStart,
                                       log.FromCompletedOn,
                                       log.At,
                                       log.Note))
            .ToListAsync(cancellationToken);

        var stageActivityLookup = stageActivity
            .GroupBy(row => row.ProjectId)
            .Select(group => new
            {
                ProjectId = group.Key,
                LastActivity = group
                    .Select(ResolveChangeDate)
                    .OrderByDescending(date => date)
                    .FirstOrDefault()
            })
            .ToDictionary(x => x.ProjectId, x => (DateOnly?)x.LastActivity);

        var remarkActivity = await _db.Remarks
            .AsNoTracking()
            .Where(r => candidateProjects.Contains(r.ProjectId))
            .Where(r => !r.IsDeleted)
            .Where(r => r.Scope == RemarkScope.General)
            .Where(r => r.EventDate <= to)
            .GroupBy(r => r.ProjectId)
            .Select(g => new { ProjectId = g.Key, LastRemark = g.Max(x => x.EventDate) })
            .ToListAsync(cancellationToken);

        foreach (var remark in remarkActivity)
        {
            if (stageActivityLookup.TryGetValue(remark.ProjectId, out var stageDate))
            {
                if (remark.LastRemark > stageDate)
                {
                    stageActivityLookup[remark.ProjectId] = remark.LastRemark;
                }
            }
            else
            {
                stageActivityLookup[remark.ProjectId] = remark.LastRemark;
            }
        }

        var stageSnapshots = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => candidateProjects.Contains(stage.ProjectId))
            .Select(stage => new StageSnapshot(
                stage.ProjectId,
                stage.StageCode,
                stage.Status,
                stage.SortOrder))
            .ToListAsync(cancellationToken);

        return baseProjects
            .Where(p => candidateProjects.Contains(p.Id))
            .Select(p =>
            {
                var stage = DetermineCurrentStage(stageSnapshots, p.Id);
                var lastActivity = stageActivityLookup.TryGetValue(p.Id, out var d) ? d : (DateOnly?)null;
                var days = lastActivity.HasValue ? Math.Max(0, to.DayNumber - lastActivity.Value.DayNumber) : (to.DayNumber - from.DayNumber + 1);
                return new ProjectNonMoverVm(
                    p.Id,
                    p.Name,
                    stage.StageCode,
                    StageCodes.DisplayNameOf(stage.StageCode),
                    days);
            })
            .OrderByDescending(p => p.DaysSinceActivity)
            .ThenBy(p => p.ProjectName)
            .ToList();
    }

    // -----------------------------------------------------------------
    // SECTION: Visits & social media
    // -----------------------------------------------------------------
    private async Task<VisitSectionVm> LoadVisitsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var visitRows = await _db.Visits
            .AsNoTracking()
            .Where(v => v.DateOfVisit >= from && v.DateOfVisit <= to)
            .OrderByDescending(v => v.DateOfVisit)
            .ThenBy(v => v.VisitorName)
            .Select(v => new
            {
                v.Id,
                v.DateOfVisit,
                v.VisitorName,
                VisitTypeName = v.VisitType != null ? v.VisitType.Name : string.Empty,
                v.Strength,
                v.Remarks,
                v.CoverPhotoId
            })
            .ToListAsync(cancellationToken);

        var items = visitRows
            .Select(v => new VisitSummaryVm(
                v.Id,
                v.DateOfVisit,
                v.VisitorName,
                v.VisitTypeName,
                v.Strength,
                Truncate(v.Remarks, 240),
                v.CoverPhotoId))
            .ToList();

        return new VisitSectionVm(items, items.Count);
    }

    private async Task<SocialMediaSectionVm> LoadSocialMediaAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var postRows = await _db.SocialMediaEvents
            .AsNoTracking()
            .Where(e => e.DateOfEvent >= from && e.DateOfEvent <= to)
            .OrderByDescending(e => e.DateOfEvent)
            .ThenBy(e => e.Title)
            .Select(e => new
            {
                e.Id,
                e.DateOfEvent,
                e.Title,
                PlatformName = e.SocialMediaPlatform != null ? e.SocialMediaPlatform.Name : string.Empty,
                e.Description,
                e.CoverPhotoId
            })
            .ToListAsync(cancellationToken);

        var posts = postRows
            .Select(e => new SocialMediaPostVm(
                e.Id,
                e.DateOfEvent,
                e.Title,
                e.PlatformName,
                Truncate(e.Description, 240),
                e.CoverPhotoId))
            .ToList();

        return new SocialMediaSectionVm(posts, posts.Count);
    }

    // -----------------------------------------------------------------
    // SECTION: ToT stage & remarks
    // -----------------------------------------------------------------
    private static IReadOnlyList<TotStageChangeVm> LoadTotStageChanges(
        IEnumerable<StageChangeLogRow> stageChanges)
    {
        return stageChanges
            .Where(r => string.Equals(r.StageCode, StageCodes.TOT, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.ChangeDate)
            .Select(r => new TotStageChangeVm(
                r.ProjectId,
                r.ProjectName,
                r.StageCode,
                StageCodes.DisplayNameOf(r.StageCode),
                r.FromStatus,
                r.ToStatus,
                r.ChangeDate))
            .ToList();
    }

    private async Task<IReadOnlyList<TotRemarkVm>> LoadTotRemarksAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var remarkRows = await _db.Remarks
            .AsNoTracking()
            .Where(r => r.Scope == RemarkScope.TransferOfTechnology)
            .Where(r => !r.IsDeleted)
            .Where(r => r.EventDate >= from && r.EventDate <= to)
            .Where(r => r.Project != null && r.Project.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Select(r => new
            {
                r.ProjectId,
                ProjectName = r.Project!.Name,
                r.EventDate,
                r.Body
            })
            .ToListAsync(cancellationToken);

        var remarks = remarkRows
            .Select(r => new TotRemarkVm(
                r.ProjectId,
                r.ProjectName,
                r.EventDate,
                Truncate(r.Body, 220)))
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.ProjectName)
            .ToList();

        return remarks;
    }

    // -----------------------------------------------------------------
    // SECTION: IPR
    // -----------------------------------------------------------------
    private async Task<IReadOnlyList<IprStatusChangeVm>> LoadIprStatusChangesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(EndOfDay), TimeSpan.Zero);

        var records = await _db.IprRecords
            .AsNoTracking()
            .Where(record =>
                (record.FiledAtUtc.HasValue && record.FiledAtUtc.Value >= fromOffset && record.FiledAtUtc.Value <= toOffset)
                || (record.GrantedAtUtc.HasValue && record.GrantedAtUtc.Value >= fromOffset && record.GrantedAtUtc.Value <= toOffset))
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Status,
                record.Notes,
                record.FiledAtUtc,
                record.GrantedAtUtc
            })
            .ToListAsync(cancellationToken);

        var items = new List<IprStatusChangeVm>();
        foreach (var record in records)
        {
            if (record.FiledAtUtc.HasValue)
            {
                var filedDate = DateOnly.FromDateTime(record.FiledAtUtc.Value.UtcDateTime);
                if (filedDate >= from && filedDate <= to)
                {
                    items.Add(new IprStatusChangeVm(
                        record.Id,
                        record.Title ?? string.Empty,
                        IprStatus.Filed,
                        filedDate,
                        Truncate(record.Notes, 220)));
                }
            }

            if (record.GrantedAtUtc.HasValue)
            {
                var grantedDate = DateOnly.FromDateTime(record.GrantedAtUtc.Value.UtcDateTime);
                if (grantedDate >= from && grantedDate <= to)
                {
                    items.Add(new IprStatusChangeVm(
                        record.Id,
                        record.Title ?? string.Empty,
                        IprStatus.Granted,
                        grantedDate,
                        Truncate(record.Notes, 220)));
                }
            }
        }

        return items
            .OrderByDescending(x => x.EventDate)
            .ThenBy(x => x.Title)
            .ToList();
    }

    private async Task<IReadOnlyList<IprRemarkVm>> LoadIprRemarksAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(EndOfDay), TimeSpan.Zero);

        var records = await _db.IprRecords
            .AsNoTracking()
            .Where(record => !string.IsNullOrWhiteSpace(record.Notes))
            .Where(record =>
                (record.GrantedAtUtc.HasValue
                    && record.GrantedAtUtc.Value >= fromOffset
                    && record.GrantedAtUtc.Value <= toOffset)
                || (!record.GrantedAtUtc.HasValue
                    && record.FiledAtUtc.HasValue
                    && record.FiledAtUtc.Value >= fromOffset
                    && record.FiledAtUtc.Value <= toOffset))
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Notes,
                record.FiledAtUtc,
                record.GrantedAtUtc
            })
            .ToListAsync(cancellationToken);

        var remarks = new List<IprRemarkVm>();
        foreach (var record in records)
        {
            var eventDateTime = record.GrantedAtUtc ?? record.FiledAtUtc;
            if (!eventDateTime.HasValue)
            {
                continue;
            }

            var occurrence = DateOnly.FromDateTime(eventDateTime.Value.UtcDateTime);
            if (occurrence < from || occurrence > to)
            {
                continue;
            }

            remarks.Add(new IprRemarkVm(
                record.Id,
                record.Title ?? string.Empty,
                occurrence,
                Truncate(record.Notes, 220)));
        }

        return remarks
            .OrderByDescending(r => r.EventDate)
            .ThenBy(r => r.Title)
            .ToList();
    }

    // -----------------------------------------------------------------
    // SECTION: Training
    // -----------------------------------------------------------------
    private async Task<TrainingBlockVm> LoadTrainingBlockAsync(
        DateOnly from,
        DateOnly to,
        Guid trainingTypeId,
        CancellationToken cancellationToken)
    {
        var fromMonthIndex = (from.Year * 12) + from.Month;
        var toMonthIndex = (to.Year * 12) + to.Month;

        var rows = await _db.Trainings
            .AsNoTracking()
            .Where(t => t.TrainingTypeId == trainingTypeId)
            .Where(t =>
                (t.StartDate.HasValue && t.StartDate.Value >= from && t.StartDate.Value <= to)
                || (t.EndDate.HasValue && t.EndDate.Value >= from && t.EndDate.Value <= to)
                || (!t.StartDate.HasValue && !t.EndDate.HasValue && t.TrainingYear.HasValue && t.TrainingMonth.HasValue
                    && (t.TrainingYear.Value * 12 + t.TrainingMonth.Value) >= fromMonthIndex
                    && (t.TrainingYear.Value * 12 + t.TrainingMonth.Value) <= toMonthIndex))
            .Select(t => new TrainingProjection(
                t.Id,
                t.TrainingType != null ? t.TrainingType.Name : string.Empty,
                t.StartDate,
                t.EndDate,
                t.TrainingMonth,
                t.TrainingYear,
                t.Counters != null ? t.Counters.Total : (int?)null,
                t.LegacyOfficerCount,
                t.LegacyJcoCount,
                t.LegacyOrCount,
                t.Notes,
                t.ProjectLinks.Select(link => link.Project != null ? link.Project.Name : null).ToList()))
            .ToListAsync(cancellationToken);

        var filtered = rows
            .Select(row =>
            {
                var occurrence = ResolveTrainingDate(row);
                return new { Row = row, Occurrence = occurrence };
            })
            .Where(tuple => tuple.Occurrence.HasValue && tuple.Occurrence.Value >= from && tuple.Occurrence.Value <= to)
            .Select(tuple => tuple)
            .OrderByDescending(tuple => tuple.Occurrence)
            .Select(tuple => tuple.Row)
            .ToList();

        var items = filtered
            .Select(row => new TrainingRowVm(
                row.Id,
                ResolveTrainingDate(row) ?? from,
                row.Title,
                BuildTrainingUnitLabel(row),
                row.Total ?? (row.Officer + row.Jco + row.Or)))
            .ToList();

        var totalPersons = items.Sum(i => i.Persons);
        return new TrainingBlockVm(totalPersons, items);
    }

    // -----------------------------------------------------------------
    // SECTION: Proliferation / FFC / Misc
    // -----------------------------------------------------------------
    private async Task<ProliferationSectionVm> LoadProliferationAsync(
        DateOnly rangeFrom,
        DateOnly rangeTo,
        CancellationToken cancellationToken)
    {
        var rows = await (from entry in _db.ProliferationGranularEntries.AsNoTracking()
                          where entry.ProliferationDate >= rangeFrom && entry.ProliferationDate <= rangeTo
                          join project in _db.Projects.AsNoTracking()
                              on entry.ProjectId equals project.Id into projectGroup
                          from project in projectGroup.DefaultIfEmpty()
                          select new ProliferationRowVm(
                              entry.Id,
                              entry.ProjectId,
                              project != null ? project.Name : $"Project {entry.ProjectId}",
                              entry.UnitName,
                              entry.Source,
                              entry.ApprovalStatus,
                              entry.ProliferationDate,
                              entry.Quantity,
                              entry.Remarks))
            .ToListAsync(cancellationToken);

        return new ProliferationSectionVm(rows
            .OrderByDescending(row => row.Date)
            .ThenBy(row => row.ProjectName)
            .ToList());
    }

    private async Task<FfcSectionVm> LoadFfcAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var entries = await _db.FfcRecords
            .AsNoTracking()
            .Where(record => !record.IsDeleted)
            .Select(record => new
            {
                record.Id,
                Country = record.Country != null ? record.Country.Name : string.Empty,
                record.IpaYes,
                record.IpaDate,
                record.IpaRemarks,
                record.GslYes,
                record.GslDate,
                record.GslRemarks,
                record.DeliveryYes,
                record.DeliveryDate,
                record.DeliveryRemarks,
                record.InstallationYes,
                record.InstallationDate,
                record.InstallationRemarks
            })
            .ToListAsync(cancellationToken);

        var rows = new List<FfcProgressVm>();
        foreach (var entry in entries)
        {
            AppendFfcRow(rows, entry.Id, entry.Country, "IPA cleared", entry.IpaDate, entry.IpaRemarks, entry.IpaYes, from, to);
            AppendFfcRow(rows, entry.Id, entry.Country, "GSL", entry.GslDate, entry.GslRemarks, entry.GslYes, from, to);
            AppendFfcRow(rows, entry.Id, entry.Country, "Delivery", entry.DeliveryDate, entry.DeliveryRemarks, entry.DeliveryYes, from, to);
            AppendFfcRow(rows, entry.Id, entry.Country, "Installation", entry.InstallationDate, entry.InstallationRemarks, entry.InstallationYes, from, to);
        }

        return new FfcSectionVm(rows
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.Country)
            .ToList());
    }

    private async Task<MiscSectionVm> LoadMiscActivitiesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await _db.Activities
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.Description,
                a.Location,
                a.ScheduledStartUtc,
                a.ScheduledEndUtc,
                PhotoStorageKey = a.Attachments
                    .Where(att => att.ContentType != null && att.ContentType.StartsWith("image/"))
                    .OrderByDescending(att => att.UploadedAtUtc)
                    .Select(att => att.StorageKey)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var result = rows
            .Select(row => new
            {
                row.Id,
                row.Title,
                row.Description,
                row.Location,
                row.PhotoStorageKey,
                Occurrence = ResolveActivityDate(row.ScheduledStartUtc, row.ScheduledEndUtc)
            })
            .Where(x => x.Occurrence.HasValue && x.Occurrence.Value >= from && x.Occurrence.Value <= to)
            .OrderByDescending(x => x.Occurrence)
            .Select(x => new MiscActivityVm(
                x.Id,
                x.Occurrence ?? from,
                x.Title,
                Truncate(x.Description, 240),
                x.Location,
                BuildAttachmentUrl(x.PhotoStorageKey)))
            .ToList();

        return new MiscSectionVm(result);
    }

    // -----------------------------------------------------------------
    // SECTION: Helpers
    // -----------------------------------------------------------------
    private static IReadOnlyList<ProjectProgressRowVm> BuildProjectSummaryRows(
        IReadOnlyList<ProjectStageChangeVm> frontRunners,
        IReadOnlyList<ProjectRemarkOnlyVm> remarkOnly,
        IReadOnlyList<ProjectNonMoverVm> nonMovers,
        IReadOnlyDictionary<int, PresentStageSnapshot> presentStageLookup,
        IReadOnlyDictionary<int, ProjectRemarkSummaryVm> remarkLookup,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        var rows = new Dictionary<int, ProjectProgressRowVm>();
        var stageHistoryLookup = BuildStageMovementLookup(frontRunners, presentStageLookup, rangeFrom, rangeTo);

        foreach (var projectStages in frontRunners.GroupBy(stageChange => stageChange.ProjectId))
        {
            var projectId = projectStages.Key;
            var stageHistory = stageHistoryLookup.TryGetValue(projectId, out var history)
                ? history
                : new List<ProjectStageMovementVm>();
            var trimmed = TrimHistory(stageHistory);
            var presentStage = presentStageLookup.TryGetValue(projectId, out var snapshot)
                ? snapshot
                : PresentStageSnapshot.Empty;
            var remarkSummary = remarkLookup.TryGetValue(projectId, out var summary)
                ? summary
                : ProjectRemarkSummaryVm.Empty;

            rows[projectId] = new ProjectProgressRowVm(
                projectId,
                projectStages.First().ProjectName,
                presentStage,
                trimmed.Display,
                trimmed.Overflow,
                remarkSummary);
        }

        foreach (var remark in remarkOnly)
        {
            if (rows.TryGetValue(remark.ProjectId, out var existing))
            {
                if (existing.RemarkSummary == ProjectRemarkSummaryVm.Empty && remark.RemarkSummary != ProjectRemarkSummaryVm.Empty)
                {
                    rows[remark.ProjectId] = existing with { RemarkSummary = remark.RemarkSummary };
                }

                continue;
            }

            var stageHistory = stageHistoryLookup.TryGetValue(remark.ProjectId, out var history)
                ? history
                : new List<ProjectStageMovementVm>();
            var trimmed = TrimHistory(stageHistory);
            var presentStage = presentStageLookup.TryGetValue(remark.ProjectId, out var snapshot)
                ? snapshot
                : PresentStageSnapshot.Empty;

            rows[remark.ProjectId] = new ProjectProgressRowVm(
                remark.ProjectId,
                remark.ProjectName,
                presentStage,
                trimmed.Display,
                trimmed.Overflow,
                remark.RemarkSummary);
        }

        foreach (var idle in nonMovers)
        {
            if (rows.ContainsKey(idle.ProjectId))
            {
                continue;
            }

            var stageHistory = stageHistoryLookup.TryGetValue(idle.ProjectId, out var history)
                ? history
                : new List<ProjectStageMovementVm>();
            var trimmed = TrimHistory(stageHistory);
            var presentStage = presentStageLookup.TryGetValue(idle.ProjectId, out var snapshot)
                ? snapshot
                : PresentStageSnapshot.Empty;
            var remarkSummary = remarkLookup.TryGetValue(idle.ProjectId, out var summary)
                ? summary
                : ProjectRemarkSummaryVm.Empty;

            rows[idle.ProjectId] = new ProjectProgressRowVm(
                idle.ProjectId,
                idle.ProjectName,
                presentStage,
                trimmed.Display,
                trimmed.Overflow,
                remarkSummary);
        }

        return rows.Values
            .OrderBy(r => r.ProjectName)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<int, PresentStageSnapshot>> BuildPresentStageLookupAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, PresentStageSnapshot>();
        }

        var stageRows = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => projectIds.Contains(stage.ProjectId))
            .Select(stage => new
            {
                stage.ProjectId,
                stage.StageCode,
                stage.Status,
                stage.SortOrder,
                stage.ActualStart,
                stage.CompletedOn
            })
            .ToListAsync(cancellationToken);

        return stageRows
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => PresentStageHelper.ComputePresentStageAndAge(
                    g.Select(stage => new ProjectStageStatusSnapshot(
                        stage.StageCode,
                        stage.Status,
                        stage.SortOrder,
                        stage.ActualStart,
                        stage.CompletedOn)).ToList()));
    }

    private static Dictionary<int, List<ProjectStageMovementVm>> BuildStageMovementLookup(
        IReadOnlyList<ProjectStageChangeVm> frontRunners,
        IReadOnlyDictionary<int, PresentStageSnapshot> presentStageLookup,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        var lookup = new Dictionary<int, List<ProjectStageMovementVm>>();

        foreach (var change in frontRunners)
        {
            if (change.ToCompletedOn.HasValue && change.ToCompletedOn.Value >= rangeFrom && change.ToCompletedOn.Value <= rangeTo)
            {
                AppendMovement(change.ProjectId, new ProjectStageMovementVm(
                    change.StageName,
                    false,
                    null,
                    change.ToCompletedOn));
            }
        }

        foreach (var (projectId, snapshot) in presentStageLookup)
        {
            if (!snapshot.IsCurrentStageInProgress || !snapshot.CurrentStageStartDate.HasValue)
            {
                continue;
            }

            var startDate = snapshot.CurrentStageStartDate.Value;
            if (startDate < rangeFrom || startDate > rangeTo)
            {
                continue;
            }

            AppendMovement(projectId, new ProjectStageMovementVm(
                snapshot.CurrentStageName ?? "Stage update",
                true,
                snapshot.CurrentStageStartDate,
                null));
        }

        foreach (var entry in lookup.Values)
        {
            entry.Sort((a, b) =>
            {
                var aDate = a.IsOngoing ? a.StartedOn : a.CompletedOn;
                var bDate = b.IsOngoing ? b.StartedOn : b.CompletedOn;
                return Nullable.Compare(bDate, aDate);
            });
        }

        return lookup;

        void AppendMovement(int projectId, ProjectStageMovementVm movement)
        {
            if (!lookup.TryGetValue(projectId, out var list))
            {
                list = new List<ProjectStageMovementVm>();
                lookup[projectId] = list;
            }

            list.Add(movement);
        }
    }

    private static (List<ProjectStageMovementVm> Display, int Overflow) TrimHistory(List<ProjectStageMovementVm> history)
    {
        if (history.Count <= 3)
        {
            return (history.ToList(), 0);
        }

        var display = history.Take(3).ToList();
        return (display, history.Count - display.Count);
    }

    private static string? BuildAttachmentUrl(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return null;
        }

        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : $"/files/{normalized}";
    }

    private static (DateOnly From, DateOnly To) NormalizeRange(DateOnly from, DateOnly to)
    {
        return to < from ? (to, from) : (from, to);
    }

    private static StageSnapshot DetermineCurrentStage(IEnumerable<StageSnapshot> stages, int projectId)
    {
        var rows = stages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .ToList();

        var current = rows.FirstOrDefault(s => s.Status == StageStatus.InProgress)
                     ?? rows.FirstOrDefault(s => s.Status != StageStatus.Completed)
                     ?? rows.LastOrDefault()
                     ?? new StageSnapshot(projectId, StageCodes.FS, StageStatus.NotStarted, 0);

        return current;
    }

    private static DateOnly? ResolveTrainingDate(TrainingProjection projection)
    {
        if (projection.StartDate.HasValue)
        {
            return projection.StartDate.Value;
        }

        if (projection.EndDate.HasValue)
        {
            return projection.EndDate.Value;
        }

        if (projection.Month.HasValue && projection.Year.HasValue)
        {
            return new DateOnly(projection.Year.Value, projection.Month.Value, 1);
        }

        return null;
    }

    private static string BuildTrainingUnitLabel(TrainingProjection projection)
    {
        if (projection.LinkedProjects.Count > 0)
        {
            return string.Join(", ", projection.LinkedProjects.Where(n => !string.IsNullOrWhiteSpace(n)).Take(2));
        }

        return string.IsNullOrWhiteSpace(projection.Notes)
            ? projection.Title
            : projection.Notes!;
    }

    private static void AppendFfcRow(
        ICollection<FfcProgressVm> rows,
        long recordId,
        string country,
        string milestone,
        DateOnly? date,
        string? remarks,
        bool isYes,
        DateOnly from,
        DateOnly to)
    {
        if (!isYes || !date.HasValue)
        {
            return;
        }

        if (date.Value < from || date.Value > to)
        {
            return;
        }

        rows.Add(new FfcProgressVm(recordId, country, milestone, date.Value, Truncate(remarks, 220)));
    }

    private static DateOnly ResolveChangeDate(StageChangeProjection row)
    {
        if (row.ToActualStart.HasValue)
        {
            return row.ToActualStart.Value;
        }

        if (row.ToCompletedOn.HasValue)
        {
            return row.ToCompletedOn.Value;
        }

        if (row.FromActualStart.HasValue)
        {
            return row.FromActualStart.Value;
        }

        if (row.FromCompletedOn.HasValue)
        {
            return row.FromCompletedOn.Value;
        }

        return DateOnly.FromDateTime(row.AtUtc.DateTime);
    }

    private static DateOnly? ResolveActivityDate(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start.HasValue)
        {
            return DateOnly.FromDateTime(start.Value.DateTime);
        }

        if (end.HasValue)
        {
            return DateOnly.FromDateTime(end.Value.DateTime);
        }

        return null;
    }

    private static string? Truncate(string? input, int length)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        return input.Length <= length ? input : input.Substring(0, length).Trim() + "…";
    }

    private sealed record StageChangeProjection(
        int ProjectId,
        string ProjectName,
        string StageCode,
        string? FromStatus,
        string? ToStatus,
        DateOnly? ToActualStart,
        DateOnly? ToCompletedOn,
        DateOnly? FromActualStart,
        DateOnly? FromCompletedOn,
        DateTimeOffset AtUtc,
        string? Note);

    private sealed record StageChangeLogRow(
        int ProjectId,
        string ProjectName,
        string StageCode,
        string? FromStatus,
        string? ToStatus,
        DateOnly ChangeDate,
        DateOnly? ToActualStart,
        DateOnly? ToCompletedOn,
        string? Note);

    private sealed record StageSnapshot(
        int ProjectId,
        string StageCode,
        StageStatus Status,
        int SortOrder);

    private sealed record TrainingProjection(
        Guid Id,
        string Title,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? Month,
        int? Year,
        int? Total,
        int Officer,
        int Jco,
        int Or,
        string? Notes,
        IReadOnlyList<string?> LinkedProjects);
}
