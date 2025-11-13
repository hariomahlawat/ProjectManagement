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
            ProjectsWithRemarks: projectRemarksOnly.Count,
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
            Projects: new ProjectSectionVm(projectFrontRunners, projectRemarksOnly, projectNonMovers),
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

        var projectIds = stageChanges.Select(x => x.ProjectId).Distinct().ToArray();
        var remarkLookup = await BuildRemarkSummaryLookupAsync(projectIds, from, to, cancellationToken);

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
                remarkLookup.TryGetValue(x.ProjectId, out var remark)
                    ? remark
                    : null))
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
                row.Note))
            .ToList();
    }

    private async Task<Dictionary<int, string?>> BuildRemarkSummaryLookupAsync(
        IReadOnlyCollection<int> projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string?>();
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
                r.Body
            })
            .ToListAsync(cancellationToken);

        return remarkRows
            .GroupBy(r => r.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Summary = g
                    .OrderByDescending(x => x.EventDate)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .Select(x => Truncate(x.Body, 220))
                    .FirstOrDefault()
            })
            .ToDictionary(x => x.ProjectId, x => x.Summary);
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
                r.Body
            })
            .ToListAsync(cancellationToken);

        return remarkRows
            .GroupBy(r => new { r.ProjectId, r.ProjectName })
            .Select(g => new ProjectRemarkOnlyVm(
                g.Key.ProjectId,
                g.Key.ProjectName,
                g.Max(x => x.EventDate),
                g.OrderByDescending(x => x.EventDate)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .Select(x => Truncate(x.Body, 220))
                    .FirstOrDefault()))
            .OrderBy(x => x.ProjectName)
            .ToList();
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
                v.Remarks
            })
            .ToListAsync(cancellationToken);

        var items = visitRows
            .Select(v => new VisitSummaryVm(
                v.Id,
                v.DateOfVisit,
                v.VisitorName,
                v.VisitTypeName,
                v.Strength,
                Truncate(v.Remarks, 240)))
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
                e.Description
            })
            .ToListAsync(cancellationToken);

        var posts = postRows
            .Select(e => new SocialMediaPostVm(
                e.Id,
                e.DateOfEvent,
                e.Title,
                e.PlatformName,
                Truncate(e.Description, 240)))
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
        var records = await _db.IprRecords
            .AsNoTracking()
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
        var records = await _db.IprRecords
            .AsNoTracking()
            .Where(record => !string.IsNullOrWhiteSpace(record.Notes))
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
        var rows = await _db.Trainings
            .AsNoTracking()
            .Where(t => t.TrainingTypeId == trainingTypeId)
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
            .OrderByDescending(row => row.Date)
            .ThenBy(row => row.ProjectName)
            .ToListAsync(cancellationToken);

        return new ProliferationSectionVm(rows);
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
                a.ScheduledEndUtc
            })
            .ToListAsync(cancellationToken);

        var result = rows
            .Select(row => new
            {
                row.Id,
                row.Title,
                row.Description,
                row.Location,
                Occurrence = ResolveActivityDate(row.ScheduledStartUtc, row.ScheduledEndUtc)
            })
            .Where(x => x.Occurrence.HasValue && x.Occurrence.Value >= from && x.Occurrence.Value <= to)
            .OrderByDescending(x => x.Occurrence)
            .Select(x => new MiscActivityVm(
                x.Id,
                x.Occurrence ?? from,
                x.Title,
                Truncate(x.Description, 240),
                x.Location))
            .ToList();

        return new MiscSectionVm(result);
    }

    // -----------------------------------------------------------------
    // SECTION: Helpers
    // -----------------------------------------------------------------
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
