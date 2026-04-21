using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;

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
    private readonly IProtectedFileUrlBuilder _fileUrlBuilder;
    private readonly IFfcQueryService _ffcQueryService;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;

    public ProgressReviewService(
        ApplicationDbContext db,
        IProtectedFileUrlBuilder fileUrlBuilder,
        IFfcQueryService ffcQueryService,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _fileUrlBuilder = fileUrlBuilder ?? throw new ArgumentNullException(nameof(fileUrlBuilder));
        _ffcQueryService = ffcQueryService ?? throw new ArgumentNullException(nameof(ffcQueryService));
        _workflowStageMetadataProvider = workflowStageMetadataProvider ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
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
        var workflowVersionLookup = await BuildWorkflowVersionLookupAsync(summaryProjectIds, cancellationToken);
        var remarkLookup = await BuildRemarkSummaryLookupAsync(summaryProjectIds, from, to, cancellationToken);
        var projectCategoryLookup = await BuildProjectCategoryLookupAsync(summaryProjectIds, cancellationToken);
        var projectSummaryRows = BuildProjectSummaryRows(
            projectFrontRunners,
            projectRemarksOnly,
            projectNonMovers,
            workflowVersionLookup,
            presentStageLookup,
            remarkLookup,
            projectCategoryLookup,
            from,
            to);
        var projectReviewBuckets = BuildProjectReviewBuckets(
            projectFrontRunners,
            projectRemarksOnly,
            projectNonMovers,
            workflowVersionLookup,
            presentStageLookup,
            remarkLookup,
            projectCategoryLookup,
            from,
            to);
        // SECTION: Movement board (resolved per-stage authoritative path)
        var movedProjectIds = projectReviewBuckets.Advanced
            .Select(row => row.ProjectId)
            .Distinct()
            .ToList();
        var resolvedProjectStages = await LoadResolvedProjectStagesAsync(movedProjectIds, cancellationToken);
        var projectMovementBoard = BuildProjectMovementBoard(
            projectReviewBuckets.Advanced,
            resolvedProjectStages,
            from,
            to);
        var projectCategoryGroups = projectSummaryRows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.ProjectCategoryName) ? "Uncategorised" : row.ProjectCategoryName)
            .OrderBy(group => group.Key)
            .Select(group => new ProjectCategoryGroupVm(group.Key, group.ToList()))
            .ToList();
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
        var ffcDetailedIncompleteGroups = await _ffcQueryService.GetDetailedGroupsAsync(
            from,
            to,
            incompleteOnly: true,
            applyYearFilter: false,
            cancellationToken: cancellationToken);
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
            Projects: new ProjectSectionVm(
                projectFrontRunners,
                projectRemarksOnly,
                projectNonMovers,
                projectSummaryRows,
                projectCategoryGroups,
                projectReviewBuckets,
                projectMovementBoard),
            Visits: visits,
            SocialMedia: socialMedia,
            Tot: new TotSectionVm(totStage, totRemarks),
            Ipr: new IprSectionVm(iprStatus, iprRemarks),
            Training: new TrainingSectionVm(simulatorTraining, droneTraining),
            Proliferation: proliferation,
            Ffc: ffc,
            FfcDetailedIncompleteGroups: ffcDetailedIncompleteGroups.ToArray(),
            Misc: misc,
            Totals: totals);
    }

    // -----------------------------------------------------------------
    // SECTION: Projects (front runners, remarks only, non-movers)
    // -----------------------------------------------------------------
    private Task<IReadOnlyList<ProjectStageChangeVm>> LoadFrontRunnerProjectsAsync(
        IReadOnlyList<StageChangeLogRow> stageChanges,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (stageChanges.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ProjectStageChangeVm>>(Array.Empty<ProjectStageChangeVm>());
        }

        var rows = stageChanges
            .Where(x => x.ChangeDate.HasValue)
            .OrderByDescending(x => x.ChangeDate)
            .ThenBy(x => x.ProjectName)
            .Select(x => new ProjectStageChangeVm(
                x.ProjectId,
                x.ProjectName,
                x.StageCode,
                StageCodes.DisplayNameOf(x.StageCode),
                x.FromStatus,
                x.ToStatus,
                x.ChangeDate!.Value,
                x.ToActualStart,
                x.ToCompletedOn))
            .ToList();

        return Task.FromResult<IReadOnlyList<ProjectStageChangeVm>>(rows);
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
                              (log.At >= fromDateTime && log.At <= toDateTime)
                              || (log.ToActualStart.HasValue && log.ToActualStart.Value >= rangeFrom && log.ToActualStart.Value <= rangeTo)
                              || (log.ToCompletedOn.HasValue && log.ToCompletedOn.Value >= rangeFrom && log.ToCompletedOn.Value <= rangeTo)
                              || (log.FromActualStart.HasValue && log.FromActualStart.Value >= rangeFrom && log.FromActualStart.Value <= rangeTo)
                              || (log.FromCompletedOn.HasValue && log.FromCompletedOn.Value >= rangeFrom && log.FromCompletedOn.Value <= rangeTo)
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

        var fromDateTime = from.ToDateTime(TimeOnly.MinValue);
        var toDateTime = to.ToDateTime(EndOfDay);

        var remarkRows = await _db.Remarks
            .AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId))
            .Where(r => !r.IsDeleted)
            .Where(r => r.Scope == RemarkScope.General)
            .Where(r => r.Type == RemarkType.External)
            .Where(r =>
                (r.EventDate != default && r.EventDate >= from && r.EventDate <= to)
                || (r.EventDate == default && r.CreatedAtUtc >= fromDateTime && r.CreatedAtUtc <= toDateTime))
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
            .Select(r => new
            {
                r.ProjectId,
                EffectiveDate = ResolveRemarkEffectiveDate(r.EventDate, r.CreatedAtUtc),
                r.CreatedAtUtc,
                r.Body,
                r.AuthorRole
            })
            .Where(r => r.EffectiveDate >= from && r.EffectiveDate <= to)
            .GroupBy(r => r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => BuildRemarkSummary(g.Select(x => (x.EffectiveDate, x.CreatedAtUtc, x.Body, x.AuthorRole))));
    }

    private async Task<IReadOnlyList<ProjectRemarkOnlyVm>> LoadProjectRemarksOnlyAsync(
        DateOnly from,
        DateOnly to,
        HashSet<int> excludedProjectIds,
        CancellationToken cancellationToken)
    {
        var fromDateTime = from.ToDateTime(TimeOnly.MinValue);
        var toDateTime = to.ToDateTime(EndOfDay);

        var remarkRows = await _db.Remarks
            .AsNoTracking()
            .Where(r => r.Scope == RemarkScope.General)
            .Where(r => r.Type == RemarkType.External)
            .Where(r => !r.IsDeleted)
            .Where(r =>
                (r.EventDate != default && r.EventDate >= from && r.EventDate <= to)
                || (r.EventDate == default && r.CreatedAtUtc >= fromDateTime && r.CreatedAtUtc <= toDateTime))
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
            .Select(r => new
            {
                r.ProjectId,
                r.ProjectName,
                EffectiveDate = ResolveRemarkEffectiveDate(r.EventDate, r.CreatedAtUtc),
                r.CreatedAtUtc,
                r.Body,
                r.AuthorRole
            })
            .Where(r => r.EffectiveDate >= from && r.EffectiveDate <= to)
            .GroupBy(r => new { r.ProjectId, r.ProjectName })
            .Select(g => new ProjectRemarkOnlyVm(
                g.Key.ProjectId,
                g.Key.ProjectName,
                BuildRemarkSummary(g.Select(x => (x.EffectiveDate, x.CreatedAtUtc, x.Body, x.AuthorRole)))))
            .OrderBy(x => x.ProjectName)
            .ToList();
    }

    private static ProjectRemarkSummaryVm BuildRemarkSummary(
        IEnumerable<(DateOnly EffectiveDate, DateTime CreatedAtUtc, string Body, RemarkActorRole AuthorRole)> remarks)
    {
        var ordered = remarks
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToList();

        if (ordered.Count == 0)
        {
            return ProjectRemarkSummaryVm.Empty;
        }

        var latest = ordered[0];
        return new ProjectRemarkSummaryVm(
            latest.EffectiveDate,
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
                    days,
                    lastActivity);
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
                v.CoverPhotoId,
                DisplayPhotoId = v.CoverPhotoId
                    ?? v.Photos
                        .OrderByDescending(p => p.CreatedAtUtc)
                        .Select(p => (Guid?)p.Id)
                        .FirstOrDefault()
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
                v.CoverPhotoId,
                v.DisplayPhotoId))
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
                e.CoverPhotoId,
                DisplayPhotoId = e.CoverPhotoId
                    ?? e.Photos
                        .OrderByDescending(p => p.CreatedAtUtc)
                        .Select(p => (Guid?)p.Id)
                        .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var posts = postRows
            .Select(e => new SocialMediaPostVm(
                e.Id,
                e.DateOfEvent,
                e.Title,
                e.PlatformName,
                Truncate(e.Description, 240),
                e.CoverPhotoId,
                e.DisplayPhotoId))
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
            .Where(r => r.ChangeDate.HasValue)
            .OrderByDescending(r => r.ChangeDate)
            .Select(r => new TotStageChangeVm(
                r.ProjectId,
                r.ProjectName,
                r.StageCode,
                StageCodes.DisplayNameOf(r.StageCode),
                r.FromStatus,
                r.ToStatus,
                r.ChangeDate!.Value))
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
                Projects = record.Projects
                    .Select(project => new
                    {
                        project.Name,
                        project.Quantity,
                        project.IsDelivered,
                        project.DeliveredOn,
                        project.IsInstalled,
                        project.InstalledOn
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var rows = new List<FfcProgressVm>();
        foreach (var entry in entries)
        {
            AppendFfcRow(rows, entry.Id, entry.Country, "IPA cleared", entry.IpaDate, entry.IpaRemarks, entry.IpaYes, from, to);
            AppendFfcRow(rows, entry.Id, entry.Country, "GSL", entry.GslDate, entry.GslRemarks, entry.GslYes, from, to);

            foreach (var project in entry.Projects)
            {
                var units = project.Quantity <= 0 ? 1 : project.Quantity;
                var projectLabel = string.IsNullOrWhiteSpace(project.Name)
                    ? $"Project units ({units})"
                    : $"{project.Name} ({units} unit{(units == 1 ? string.Empty : "s")})";

                if (project.IsDelivered && project.DeliveredOn.HasValue)
                {
                    AppendFfcRow(
                        rows,
                        entry.Id,
                        entry.Country,
                        $"Delivery – {projectLabel}",
                        project.DeliveredOn,
                        remarks: null,
                        isYes: true,
                        from,
                        to);
                }

                if (project.IsInstalled && project.InstalledOn.HasValue)
                {
                    AppendFfcRow(
                        rows,
                        entry.Id,
                        entry.Country,
                        $"Installation – {projectLabel}",
                        project.InstalledOn,
                        remarks: null,
                        isYes: true,
                        from,
                        to);
                }
            }
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
                DisplayPhotoId = (Guid?)null,
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
                row.DisplayPhotoId,
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
                BuildAttachmentUrl(x.PhotoStorageKey),
                x.DisplayPhotoId))
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
        IReadOnlyDictionary<int, string?> workflowVersionLookup,
        IReadOnlyDictionary<int, PresentStageSnapshot> presentStageLookup,
        IReadOnlyDictionary<int, ProjectRemarkSummaryVm> remarkLookup,
        IReadOnlyDictionary<int, string?> projectCategoryLookup,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        var rows = new Dictionary<int, ProjectProgressRowVm>();
        var stageHistoryLookup = BuildStageMovementLookup(frontRunners, presentStageLookup, workflowVersionLookup, rangeFrom, rangeTo);

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
            var categoryName = projectCategoryLookup.TryGetValue(projectId, out var projectCategory)
                ? projectCategory
                : null;

            rows[projectId] = new ProjectProgressRowVm(
                projectId,
                projectStages.First().ProjectName,
                categoryName,
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
            var categoryName = projectCategoryLookup.TryGetValue(remark.ProjectId, out var projectCategory)
                ? projectCategory
                : null;

            rows[remark.ProjectId] = new ProjectProgressRowVm(
                remark.ProjectId,
                remark.ProjectName,
                categoryName,
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
            var categoryName = projectCategoryLookup.TryGetValue(idle.ProjectId, out var projectCategory)
                ? projectCategory
                : null;

            rows[idle.ProjectId] = new ProjectProgressRowVm(
                idle.ProjectId,
                idle.ProjectName,
                categoryName,
                presentStage,
                trimmed.Display,
                trimmed.Overflow,
                remarkSummary);
        }

        return rows.Values
            .OrderBy(r => r.ProjectName)
            .ToList();
    }

    // -----------------------------------------------------------------
    // SECTION: Project review buckets (Section 01 executive surface)
    // -----------------------------------------------------------------
    private static ProjectReviewBucketsVm BuildProjectReviewBuckets(
        IReadOnlyList<ProjectStageChangeVm> frontRunners,
        IReadOnlyList<ProjectRemarkOnlyVm> remarkOnly,
        IReadOnlyList<ProjectNonMoverVm> nonMovers,
        IReadOnlyDictionary<int, string?> workflowVersionLookup,
        IReadOnlyDictionary<int, PresentStageSnapshot> presentStageLookup,
        IReadOnlyDictionary<int, ProjectRemarkSummaryVm> remarkLookup,
        IReadOnlyDictionary<int, string?> projectCategoryLookup,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        var stageHistoryLookup = BuildStageMovementLookup(frontRunners, presentStageLookup, workflowVersionLookup, rangeFrom, rangeTo);
        var reviewRows = new Dictionary<int, ProjectReviewRowVm>();
        var nonMoverLookup = nonMovers.ToDictionary(n => n.ProjectId);

        // SECTION: Advanced bucket
        foreach (var pair in stageHistoryLookup.Where(pair => pair.Value.Count > 0))
        {
            var projectId = pair.Key;
            var movements = pair.Value;
            var projectName = frontRunners
                .FirstOrDefault(r => r.ProjectId == projectId)?.ProjectName
                ?? remarkOnly.FirstOrDefault(r => r.ProjectId == projectId)?.ProjectName
                ?? nonMovers.FirstOrDefault(r => r.ProjectId == projectId)?.ProjectName
                ?? $"Project {projectId}";

            var presentStage = presentStageLookup.TryGetValue(projectId, out var advancedSnapshot)
                ? advancedSnapshot
                : PresentStageSnapshot.Empty;
            var remarks = remarkLookup.TryGetValue(projectId, out var advancedRemarks)
                ? advancedRemarks
                : ProjectRemarkSummaryVm.Empty;
            var categoryName = projectCategoryLookup.TryGetValue(projectId, out var advancedCategory)
                ? advancedCategory
                : null;
            var lastStageMovementDate = GetLastStageMovementDate(movements);

            reviewRows[projectId] = new ProjectReviewRowVm(
                projectId,
                projectName,
                categoryName,
                presentStage,
                BuildMovementPathText(movements, workflowVersionLookup.TryGetValue(projectId, out var workflowVersion) ? workflowVersion : null),
                TrimHistory(movements).Display,
                movements,
                movements.Count,
                lastStageMovementDate,
                remarks,
                lastStageMovementDate,
                lastStageMovementDate.HasValue ? Math.Max(0, rangeTo.DayNumber - lastStageMovementDate.Value.DayNumber) : 0,
                ProjectReviewBucket.Advanced,
                ProjectAttentionStatus.Normal);
        }

        // SECTION: Active without advancement bucket
        foreach (var row in remarkOnly)
        {
            if (reviewRows.ContainsKey(row.ProjectId))
            {
                continue;
            }

            var stageMovements = stageHistoryLookup.TryGetValue(row.ProjectId, out var history)
                ? history
                : new List<ProjectStageMovementVm>();
            var presentStage = presentStageLookup.TryGetValue(row.ProjectId, out var activeSnapshot)
                ? activeSnapshot
                : PresentStageSnapshot.Empty;
            var categoryName = projectCategoryLookup.TryGetValue(row.ProjectId, out var activeCategory)
                ? activeCategory
                : null;
            var lastRecordedActivityDate = row.RemarkSummary.LatestRemarkDate
                ?? (nonMoverLookup.TryGetValue(row.ProjectId, out var nonMover) ? nonMover.LastRecordedActivityDate : null);
            var daysSinceLastRecordedActivity = lastRecordedActivityDate.HasValue
                ? Math.Max(0, rangeTo.DayNumber - lastRecordedActivityDate.Value.DayNumber)
                : 0;

            reviewRows[row.ProjectId] = new ProjectReviewRowVm(
                row.ProjectId,
                row.ProjectName,
                categoryName,
                presentStage,
                BuildMovementPathText(stageMovements, workflowVersionLookup.TryGetValue(row.ProjectId, out var workflowVersion) ? workflowVersion : null),
                TrimHistory(stageMovements).Display,
                stageMovements,
                stageMovements.Count,
                GetLastStageMovementDate(stageMovements),
                row.RemarkSummary,
                lastRecordedActivityDate,
                daysSinceLastRecordedActivity,
                ProjectReviewBucket.ActiveWithoutAdvancement,
                DetermineAttentionStatus(daysSinceLastRecordedActivity));
        }

        // SECTION: Attention bucket
        foreach (var row in nonMovers)
        {
            if (reviewRows.ContainsKey(row.ProjectId))
            {
                continue;
            }

            var stageMovements = stageHistoryLookup.TryGetValue(row.ProjectId, out var history)
                ? history
                : new List<ProjectStageMovementVm>();
            var remarks = remarkLookup.TryGetValue(row.ProjectId, out var remarkSummary)
                ? remarkSummary
                : ProjectRemarkSummaryVm.Empty;
            var presentStage = presentStageLookup.TryGetValue(row.ProjectId, out var attentionSnapshot)
                ? attentionSnapshot
                : PresentStageSnapshot.Empty;
            var categoryName = projectCategoryLookup.TryGetValue(row.ProjectId, out var attentionCategory)
                ? attentionCategory
                : null;

            reviewRows[row.ProjectId] = new ProjectReviewRowVm(
                row.ProjectId,
                row.ProjectName,
                categoryName,
                presentStage,
                BuildMovementPathText(stageMovements, workflowVersionLookup.TryGetValue(row.ProjectId, out var workflowVersion) ? workflowVersion : null),
                TrimHistory(stageMovements).Display,
                stageMovements,
                stageMovements.Count,
                GetLastStageMovementDate(stageMovements),
                remarks,
                row.LastRecordedActivityDate,
                row.DaysSinceActivity,
                ProjectReviewBucket.Attention,
                DetermineAttentionStatus(row.DaysSinceActivity));
        }

        var advancedRows = reviewRows.Values
            .Where(r => r.ReviewBucket == ProjectReviewBucket.Advanced)
            .OrderByDescending(r => r.MovementCountInRange)
            .ThenByDescending(r => r.LastStageMovementDate)
            .ThenBy(r => r.ProjectName)
            .ToList();
        var activeWithoutAdvancementRows = reviewRows.Values
            .Where(r => r.ReviewBucket == ProjectReviewBucket.ActiveWithoutAdvancement)
            .OrderByDescending(r => r.LastRecordedActivityDate)
            .ThenBy(r => r.ProjectName)
            .ToList();
        var attentionRows = reviewRows.Values
            .Where(r => r.ReviewBucket == ProjectReviewBucket.Attention)
            .OrderByDescending(r => r.DaysSinceLastRecordedActivity)
            .ThenBy(r => r.ProjectName)
            .ToList();

        var noMovementCount = activeWithoutAdvancementRows.Count + attentionRows.Count;
        var summary = new ProjectReviewSummaryVm(
            ProjectsInScope: reviewRows.Count,
            AdvancedCount: advancedRows.Count,
            ActiveWithoutAdvancementCount: activeWithoutAdvancementRows.Count,
            NoMovementCount: noMovementCount,
            AttentionCount: attentionRows.Count,
            InterpretiveSummaryText: BuildInterpretiveSummaryText(
                advancedRows.Count,
                activeWithoutAdvancementRows.Count,
                noMovementCount,
                advancedRows,
                attentionRows));

        return new ProjectReviewBucketsVm(
            Summary: summary,
            Highlights: BuildHighlights(advancedRows),
            Advanced: advancedRows,
            ActiveWithoutAdvancement: activeWithoutAdvancementRows,
            Attention: attentionRows);
    }

    private static string BuildMovementPathText(IReadOnlyList<ProjectStageMovementVm> movements, string? workflowVersion)
    {
        if (movements.Count == 0)
        {
            return "No formal stage movement recorded.";
        }

        var trimmedHistory = TrimHistory(movements);
        var orderedForPath = trimmedHistory.Display
            .OrderBy(GetMovementEventDate)
            .ThenBy(movement => GetStageSortOrder(movement.StageCode, workflowVersion))
            .ThenBy(movement => movement.StageName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(" \u2192 ", orderedForPath.Select(FormatMovementLabel));
    }

    private static DateOnly? GetMovementEventDate(ProjectStageMovementVm movement)
    {
        return movement.CompletedOn ?? movement.StartedOn;
    }

    private static DateOnly? ResolveMovementBoardDate(ProjectResolvedStageVm stage)
    {
        if (stage.CompletedOn.HasValue)
        {
            return stage.CompletedOn.Value;
        }

        if (stage.IsCurrent && stage.StartedOn.HasValue)
        {
            return stage.StartedOn.Value;
        }

        return null;
    }

    // -----------------------------------------------------------------
    // SECTION: Stage movement normalization helpers
    // -----------------------------------------------------------------
    private static int GetStageSortOrder(string? stageCode, string? workflowVersion)
    {
        return ProcurementWorkflow.OrderOf(workflowVersion, stageCode);
    }

    private static List<ProjectStageMovementVm> NormalizeMovements(
        IEnumerable<ProjectStageMovementVm> movements,
        string? workflowVersion)
    {
        return movements
            .OrderBy(GetMovementEventDate)
            .ThenBy(m => GetStageSortOrder(m.StageCode, workflowVersion))
            .ThenBy(m => m.StageName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(m => new
            {
                StageCode = (m.StageCode ?? string.Empty).Trim().ToUpperInvariant(),
                EventDate = GetMovementEventDate(m),
                m.IsOngoing
            })
            .Select(g => g.First())
            .ToList();
    }

    private static string FormatMovementLabel(ProjectStageMovementVm movement)
    {
        var suffix = movement.IsOngoing ? "started" : "completed";
        return $"{movement.StageName} {suffix}";
    }

    private static DateOnly? GetLastStageMovementDate(IReadOnlyList<ProjectStageMovementVm> movements)
    {
        var latest = movements
            .Select(movement => movement.IsOngoing ? movement.StartedOn : movement.CompletedOn)
            .Where(date => date.HasValue)
            .OrderByDescending(date => date)
            .FirstOrDefault();

        return latest;
    }

    private static ProjectAttentionStatus DetermineAttentionStatus(int daysSinceLastActivity)
    {
        return daysSinceLastActivity switch
        {
            >= 180 => ProjectAttentionStatus.LongPending,
            >= 120 => ProjectAttentionStatus.Delayed,
            >= 60 => ProjectAttentionStatus.Watch,
            _ => ProjectAttentionStatus.Normal
        };
    }

    private static string? BuildInterpretiveSummaryText(
        int advancedCount,
        int activeWithoutAdvancementCount,
        int noMovementCount,
        IReadOnlyList<ProjectReviewRowVm> advancedRows,
        IReadOnlyList<ProjectReviewRowVm> attentionRows)
    {
        if (advancedCount == 0 && noMovementCount == 0)
        {
            return null;
        }

        if (advancedCount == 0)
        {
            return "The selected period recorded no formal stage movement, with attention focused on projects awaiting renewed activity.";
        }

        if (attentionRows.Any(row => row.AttentionStatus == ProjectAttentionStatus.LongPending))
        {
            return "Movement was recorded in the reporting period, but several projects remain long-pending and require management attention.";
        }

        if (advancedRows.Any(row => row.MovementCountInRange >= 2))
        {
            return "Most movement in the selected period came from projects that progressed through multiple stage updates.";
        }

        if (activeWithoutAdvancementCount > advancedCount)
        {
            return "The period showed broader activity through remarks, but formal stage advancement remained limited.";
        }

        return "The selected period recorded measurable stage advancement with a manageable set of projects requiring closer follow-up.";
    }

    private static IReadOnlyList<ProjectReviewHighlightVm> BuildHighlights(
        IReadOnlyList<ProjectReviewRowVm> advancedRows)
    {
        return advancedRows
            .OrderByDescending(row => row.MovementCountInRange)
            .ThenByDescending(row => row.LastStageMovementDate)
            .ThenBy(row => row.ProjectName)
            .Take(5)
            .Select(row => new ProjectReviewHighlightVm(
                row.ProjectId,
                row.ProjectName,
                row.ProjectCategoryName,
                BuildHighlightText(row),
                row.LastStageMovementDate,
                row.MovementCountInRange))
            .ToList();

        static string BuildHighlightText(ProjectReviewRowVm row)
        {
            if (row.MovementCountInRange >= 2)
            {
                return $"{row.ProjectName} registered multi-stage progression during the reporting period.";
            }

            return $"{row.ProjectName} recorded formal movement in the selected period.";
        }
    }

    // -----------------------------------------------------------------
    // SECTION: Project movement board helpers
    // -----------------------------------------------------------------
    private static ProjectMovementBoardVm BuildProjectMovementBoard(
        IReadOnlyList<ProjectReviewRowVm> advancedRows,
        IReadOnlyList<ProjectResolvedStageVm> resolvedStages,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        // SECTION: Authoritative resolved stage map
        var resolvedByProject = resolvedStages
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(group => group.Key, group => group.OrderBy(stage => stage.StageOrder).ToList());

        var rows = advancedRows
            .Select(row =>
            {
                if (!resolvedByProject.TryGetValue(row.ProjectId, out var projectStages))
                {
                    projectStages = [];
                }

                // SECTION: Filter to period using resolved stage dates
                var includedStages = projectStages
                    .Where(stage =>
                        (stage.CompletedOn.HasValue && stage.CompletedOn.Value >= rangeFrom && stage.CompletedOn.Value <= rangeTo)
                        || (stage.IsCurrent && stage.StartedOn.HasValue && stage.StartedOn.Value >= rangeFrom && stage.StartedOn.Value <= rangeTo))
                    .OrderBy(stage => stage.StageOrder)
                    .ToList();

                var movementSteps = includedStages
                    .Select((stage, index) => new ProjectMovementStepVm(
                        stage.StageCode,
                        stage.StageName,
                        ResolveMovementBoardDate(stage),
                        index == includedStages.Count - 1))
                    .ToList();

                var firstMovementDate = movementSteps
                    .Select(step => step.EventDate)
                    .Where(date => date.HasValue)
                    .OrderBy(date => date)
                    .FirstOrDefault();

                return new ProjectMovementRowVm(
                    row.ProjectId,
                    row.ProjectName,
                    row.ProjectCategoryName,
                    movementSteps,
                    row.MovementCountInRange,
                    firstMovementDate,
                    row.PresentStage.CurrentStageName);
            })
            .Where(row => row.Steps.Count > 0)
            .OrderByDescending(r => r.MovementCount)
            .ThenByDescending(r => r.FirstMovementDate)
            .ThenBy(r => r.ProjectName)
            .ToList();

        return new ProjectMovementBoardVm(rows, rows.Count);
    }

    private async Task<IReadOnlyList<ProjectResolvedStageVm>> LoadResolvedProjectStagesAsync(
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        // SECTION: Workflow version lookup for display-name resolution
        var workflowVersions = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.WorkflowVersion, cancellationToken);

        // SECTION: Resolved stage-state source (not historical stage-change logs)
        var rows = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => projectIds.Contains(stage.ProjectId))
            .Select(stage => new
            {
                stage.ProjectId,
                stage.StageCode,
                stage.SortOrder,
                stage.ActualStart,
                stage.CompletedOn,
                stage.Status
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(stage =>
            {
                workflowVersions.TryGetValue(stage.ProjectId, out var workflowVersion);
                var stageName = _workflowStageMetadataProvider.GetDisplayName(workflowVersion, stage.StageCode);

                return new ProjectResolvedStageVm(
                    stage.ProjectId,
                    stage.StageCode,
                    stageName,
                    stage.SortOrder,
                    stage.ActualStart,
                    stage.CompletedOn,
                    stage.Status == StageStatus.InProgress);
            })
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

        var workflowVersions = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.WorkflowVersion, cancellationToken);

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
                g =>
                {
                    workflowVersions.TryGetValue(g.Key, out var workflowVersion);

                    return PresentStageHelper.ComputePresentStageAndAge(
                        g.Select(stage => new ProjectStageStatusSnapshot(
                            stage.StageCode,
                            stage.Status,
                            stage.SortOrder,
                            stage.ActualStart,
                            stage.CompletedOn)).ToList(),
                        _workflowStageMetadataProvider,
                        workflowVersion);
                });
    }

    private async Task<IReadOnlyDictionary<int, string?>> BuildProjectCategoryLookupAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string?>();
        }

        var rows = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new
            {
                project.Id,
                CategoryName = project.Category != null ? project.Category.Name : null
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => row.CategoryName);
    }

    private static Dictionary<int, List<ProjectStageMovementVm>> BuildStageMovementLookup(
        IReadOnlyList<ProjectStageChangeVm> frontRunners,
        IReadOnlyDictionary<int, PresentStageSnapshot> presentStageLookup,
        IReadOnlyDictionary<int, string?> workflowVersionLookup,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        var lookup = new Dictionary<int, List<ProjectStageMovementVm>>();

        foreach (var change in frontRunners)
        {
            if (change.ToCompletedOn.HasValue && change.ToCompletedOn.Value >= rangeFrom && change.ToCompletedOn.Value <= rangeTo)
            {
                workflowVersionLookup.TryGetValue(change.ProjectId, out var workflowVersion);
                AppendMovement(change.ProjectId, new ProjectStageMovementVm(
                    change.StageCode,
                    change.StageName,
                    workflowVersion,
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
                snapshot.CurrentStageCode ?? string.Empty,
                snapshot.CurrentStageName ?? "Stage update",
                workflowVersionLookup.TryGetValue(projectId, out var workflowVersion) ? workflowVersion : null,
                true,
                snapshot.CurrentStageStartDate,
                null));
        }

        foreach (var projectId in lookup.Keys.ToList())
        {
            lookup[projectId] = NormalizeMovements(
                lookup[projectId],
                workflowVersionLookup.TryGetValue(projectId, out var workflowVersion) ? workflowVersion : null);
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

    private static (List<ProjectStageMovementVm> Display, int Overflow) TrimHistory(IReadOnlyList<ProjectStageMovementVm> history)
    {
        if (history.Count <= 3)
        {
            return (history.ToList(), 0);
        }

        var display = history
            .OrderBy(movement => GetMovementEventDate(movement))
            .ThenBy(movement => GetStageSortOrder(movement.StageCode, movement.WorkflowVersion))
            .ThenBy(movement => movement.StageName, StringComparer.OrdinalIgnoreCase)
            .TakeLast(3)
            .ToList();
        return (display, history.Count - display.Count);
    }

    private async Task<IReadOnlyDictionary<int, string?>> BuildWorkflowVersionLookupAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string?>();
        }

        return await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.WorkflowVersion, cancellationToken);
    }

    private string? BuildAttachmentUrl(string? storageKey)
    {
        return string.IsNullOrWhiteSpace(storageKey)
            ? null
            : _fileUrlBuilder.CreateDownloadUrl(storageKey);
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

    private static DateOnly? ResolveChangeDate(StageChangeProjection row)
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

        return null;
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

    private static DateOnly ResolveRemarkEffectiveDate(DateOnly eventDate, DateTime createdAtUtc)
    {
        if (eventDate != default)
        {
            return eventDate;
        }

        var createdIst = IstClock.ToIst(createdAtUtc);
        return DateOnly.FromDateTime(createdIst);
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
        DateOnly? ChangeDate,
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
