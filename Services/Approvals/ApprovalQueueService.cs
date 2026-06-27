using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models.Activities;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Authorization;
using ProjectManagement.Services.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Approvals;

public sealed class ApprovalQueueService : IApprovalQueueService
{
    private const string PendingDecisionStatus = "Pending";

    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly PlanCompareService _planCompareService;
    private readonly StageApprovalSequenceService _stageSequence;

    public ApprovalQueueService(
        ApplicationDbContext db,
        PlanCompareService planCompareService,
        StageApprovalSequenceService stageSequence)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _planCompareService = planCompareService ?? throw new ArgumentNullException(nameof(planCompareService));
        _stageSequence = stageSequence ?? throw new ArgumentNullException(nameof(stageSequence));
    }

    // SECTION: Pending approvals list
    public async Task<IReadOnlyList<ApprovalQueueItemVm>> GetPendingAsync(
        ApprovalQueueQuery query,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (!ApprovalAuthorization.CanApproveProjectChanges(user.IsInRole("Admin"), user.IsInRole("HoD")))
        {
            return Array.Empty<ApprovalQueueItemVm>();
        }

        query ??= new ApprovalQueueQuery();

        var results = new List<ApprovalQueueItemVm>();

        if (ShouldIncludeType(query, ApprovalQueueType.StageChange))
        {
            var stageRows = await BuildStageChangeQuery(query)
                .ToListAsync(cancellationToken);
            var assessments = await _stageSequence.AssessPendingAsync(
                stageRows.Select(row => row.ProjectId),
                cancellationToken);

            results.AddRange(stageRows.Select(row =>
            {
                var item = MapStageChangeRow(row);
                return assessments.TryGetValue(row.Id, out var assessment)
                    ? ApplyStageAssessment(item, assessment)
                    : item with
                    {
                        Readiness = ApprovalReadiness.Stale,
                        ReadinessMessage = "The stage request could not be evaluated."
                    };
            }));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.ProjectMeta))
        {
            var metaRows = await BuildMetaChangeQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(metaRows.Select(MapMetaChangeRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.PlanApproval))
        {
            var planRows = await BuildPlanApprovalQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(planRows.Select(MapPlanApprovalRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.DocRequest))
        {
            var docRows = await BuildDocumentRequestQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(docRows.Select(MapDocumentRequestRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.TotRequest))
        {
            var totRows = await BuildTotRequestQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(totRows.Select(MapTotRequestRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.ProliferationYearly))
        {
            var yearlyRows = await BuildProliferationYearlyQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(yearlyRows.Select(MapProliferationYearlyRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.ProliferationGranular))
        {
            var granularRows = await BuildProliferationGranularQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(granularRows.Select(MapProliferationGranularRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.ActivityDelete))
        {
            var activityRows = await BuildActivityDeleteQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(activityRows.Select(MapActivityDeleteRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.TrainingDelete))
        {
            var trainingRows = await BuildTrainingDeleteQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(trainingRows.Select(MapTrainingDeleteRow));
        }

        if (ShouldIncludeType(query, ApprovalQueueType.RepositoryDocumentDelete))
        {
            var repositoryRows = await BuildRepositoryDocumentDeleteQuery(query)
                .ToListAsync(cancellationToken);
            results.AddRange(repositoryRows.Select(MapRepositoryDocumentDeleteRow));
        }

        IEnumerable<ApprovalQueueItemVm> filtered = results;
        if (query.Module.HasValue)
        {
            filtered = filtered.Where(item => item.Module == query.Module.Value);
        }

        if (query.Readiness.HasValue)
        {
            filtered = filtered.Where(item => item.Readiness == query.Readiness.Value);
        }

        return filtered
            .OrderBy(item => ReadinessOrder(item.Readiness))
            .ThenBy(item => item.ProjectName ?? string.Empty)
            .ThenBy(item => item.WorkflowOrder ?? int.MaxValue)
            .ThenBy(item => item.RequestedAtUtc)
            .ToList();
    }

    // SECTION: Pending approvals count
    public async Task<int> GetPendingCountAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (!ApprovalAuthorization.CanApproveProjectChanges(user.IsInRole("Admin"), user.IsInRole("HoD")))
        {
            return 0;
        }

        var query = new ApprovalQueueQuery();
        var total = 0;

        total += await BuildStageChangeQuery(query).CountAsync(cancellationToken);
        total += await BuildMetaChangeQuery(query).CountAsync(cancellationToken);
        total += await BuildPlanApprovalQuery(query).CountAsync(cancellationToken);
        total += await BuildDocumentRequestQuery(query).CountAsync(cancellationToken);
        total += await BuildTotRequestQuery(query).CountAsync(cancellationToken);
        total += await BuildProliferationYearlyQuery(query).CountAsync(cancellationToken);
        total += await BuildProliferationGranularQuery(query).CountAsync(cancellationToken);
        total += await BuildActivityDeleteQuery(query).CountAsync(cancellationToken);
        total += await BuildTrainingDeleteQuery(query).CountAsync(cancellationToken);
        total += await BuildRepositoryDocumentDeleteQuery(query).CountAsync(cancellationToken);

        return total;
    }

    // SECTION: Pending approval detail
    public async Task<ApprovalQueueDetailVm?> GetDetailAsync(
        ApprovalQueueType type,
        string requestId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (!ApprovalAuthorization.CanApproveProjectChanges(user.IsInRole("Admin"), user.IsInRole("HoD")))
        {
            return null;
        }

        return type switch
        {
            ApprovalQueueType.StageChange => await BuildStageChangeDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.ProjectMeta => await BuildMetaChangeDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.PlanApproval => await BuildPlanApprovalDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.DocRequest => await BuildDocumentModerationDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.TotRequest => await BuildTotRequestDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.ProliferationYearly => await BuildProliferationYearlyDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.ProliferationGranular => await BuildProliferationGranularDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.ActivityDelete => await BuildActivityDeleteDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.TrainingDelete => await BuildTrainingDeleteDetailAsync(requestId, cancellationToken),
            ApprovalQueueType.RepositoryDocumentDelete => await BuildRepositoryDocumentDeleteDetailAsync(requestId, cancellationToken),
            _ => null
        };
    }

    // SECTION: Stage change list
    private IQueryable<StageChangeRow> BuildStageChangeQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from req in _db.StageChangeRequests.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on req.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on req.RequestedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where req.DecisionStatus == PendingDecisionStatus
                        select new StageChangeRow(
                            req.Id,
                            req.ProjectId,
                            project.Name,
                            project.WorkflowVersion,
                            req.StageCode,
                            req.RequestedStatus,
                            req.RequestedDate,
                            req.RequestedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            req.RequestedOn);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapStageChangeRow(StageChangeRow row)
    {
        var stageName = StageCodes.DisplayNameOf(row.WorkflowVersion, row.StageCode);
        var summary = row.RequestedDate.HasValue
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0} stage to {1} ({2:dd MMM yyyy})",
                stageName,
                row.RequestedStatus,
                row.RequestedDate.Value)
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} stage to {1}",
                stageName,
                row.RequestedStatus);

        return new ApprovalQueueItemVm(
            ApprovalQueueType.StageChange,
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.ProjectId,
            row.ProjectName,
            row.RequestedByUserId,
            ResolveUserDisplayName(row.RequestedByFullName, row.RequestedByUserName, row.RequestedByEmail),
            row.RequestedOn,
            summary,
            ApprovalQueueModule.Projects,
            PendingDecisionStatus,
            null,
            null);
    }

    // SECTION: Meta change list
    private IQueryable<MetaChangeRow> BuildMetaChangeQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from req in _db.ProjectMetaChangeRequests.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on req.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on req.RequestedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where req.DecisionStatus == ProjectMetaDecisionStatuses.Pending
                        select new MetaChangeRow(
                            req.Id,
                            req.ProjectId,
                            project.Name,
                            req.RequestedByUserId ?? string.Empty,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            req.RequestedOnUtc);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapMetaChangeRow(MetaChangeRow row)
    {
        return new ApprovalQueueItemVm(
            ApprovalQueueType.ProjectMeta,
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.ProjectId,
            row.ProjectName,
            row.RequestedByUserId,
            ResolveUserDisplayName(row.RequestedByFullName, row.RequestedByUserName, row.RequestedByEmail),
            row.RequestedOnUtc,
            "Project metadata change request.",
            ApprovalQueueModule.Projects,
            ProjectMetaDecisionStatuses.Pending,
            null,
            null);
    }

    // SECTION: Plan approval list
    private IQueryable<PlanApprovalRow> BuildPlanApprovalQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from plan in _db.PlanVersions.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on plan.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on plan.SubmittedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where plan.Status == PlanVersionStatus.PendingApproval
                        select new PlanApprovalRow(
                            plan.Id,
                            plan.ProjectId,
                            project.Name,
                            plan.VersionNo,
                            plan.SubmittedByUserId ?? string.Empty,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            plan.SubmittedOn ?? plan.CreatedOn);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapPlanApprovalRow(PlanApprovalRow row)
    {
        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Plan version {0} awaiting approval.",
            row.VersionNo);

        return new ApprovalQueueItemVm(
            ApprovalQueueType.PlanApproval,
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.ProjectId,
            row.ProjectName,
            row.SubmittedByUserId,
            ResolveUserDisplayName(row.SubmittedByFullName, row.SubmittedByUserName, row.SubmittedByEmail),
            row.SubmittedOnUtc,
            summary,
            ApprovalQueueModule.Projects,
            PlanVersionStatus.PendingApproval.ToString(),
            null,
            null);
    }

    // SECTION: Document moderation list
    private IQueryable<DocumentRequestRow> BuildDocumentRequestQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from req in _db.ProjectDocumentRequests.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on req.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on req.RequestedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where req.Status == ProjectDocumentRequestStatus.Submitted
                        select new DocumentRequestRow(
                            req.Id,
                            req.ProjectId,
                            project.Name,
                            req.RequestType,
                            req.Title,
                            req.RequestedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            req.RequestedAtUtc);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapDocumentRequestRow(DocumentRequestRow row)
    {
        var summary = row.RequestType switch
        {
            ProjectDocumentRequestType.Replace => $"Replace document: {row.Title}",
            ProjectDocumentRequestType.Delete => $"Delete document: {row.Title}",
            _ => $"Publish new document: {row.Title}"
        };

        return new ApprovalQueueItemVm(
            ApprovalQueueType.DocRequest,
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.ProjectId,
            row.ProjectName,
            row.RequestedByUserId,
            ResolveUserDisplayName(row.RequestedByFullName, row.RequestedByUserName, row.RequestedByEmail),
            row.RequestedAtUtc,
            summary,
            ApprovalQueueModule.Projects,
            ProjectDocumentRequestStatus.Submitted.ToString(),
            null,
            null);
    }

    // SECTION: ToT request list
    private IQueryable<TotRequestRow> BuildTotRequestQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from req in _db.ProjectTotRequests.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on req.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on req.SubmittedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where req.DecisionState == ProjectTotRequestDecisionState.Pending
                        select new TotRequestRow(
                            req.Id,
                            req.ProjectId,
                            project.Name,
                            req.ProposedStatus,
                            req.SubmittedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            req.SubmittedOnUtc,
                            req.RowVersion);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapTotRequestRow(TotRequestRow row)
    {
        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Transfer of Technology status update to {0}.",
            row.ProposedStatus);

        return new ApprovalQueueItemVm(
            ApprovalQueueType.TotRequest,
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.ProjectId,
            row.ProjectName,
            row.SubmittedByUserId,
            ResolveUserDisplayName(row.SubmittedByFullName, row.SubmittedByUserName, row.SubmittedByEmail),
            row.SubmittedOnUtc,
            summary,
            ApprovalQueueModule.ProjectOfficeReports,
            ProjectTotRequestDecisionState.Pending.ToString(),
            null,
            Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()));
    }

    // SECTION: Proliferation yearly list
    private IQueryable<ProliferationYearlyRow> BuildProliferationYearlyQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from yearly in _db.ProliferationYearlies.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on yearly.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on yearly.SubmittedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where yearly.ApprovalStatus == ApprovalStatus.Pending
                        select new ProliferationYearlyRow(
                            yearly.Id,
                            yearly.ProjectId,
                            project.Name,
                            yearly.Source,
                            yearly.Year,
                            yearly.TotalQuantity,
                            yearly.SubmittedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            yearly.CreatedOnUtc,
                            yearly.RowVersion);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapProliferationYearlyRow(ProliferationYearlyRow row)
    {
        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} yearly proliferation {1}: {2}",
            row.Source.ToDisplayName(),
            row.Year,
            row.TotalQuantity);

        return new ApprovalQueueItemVm(
            ApprovalQueueType.ProliferationYearly,
            row.Id.ToString(),
            row.ProjectId,
            row.ProjectName,
            row.SubmittedByUserId,
            ResolveUserDisplayName(row.SubmittedByFullName, row.SubmittedByUserName, row.SubmittedByEmail),
            row.CreatedOnUtc,
            summary,
            ApprovalQueueModule.ProjectOfficeReports,
            ApprovalStatus.Pending.ToString(),
            null,
            Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()));
    }

    // SECTION: Proliferation granular list
    private IQueryable<ProliferationGranularRow> BuildProliferationGranularQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from granular in _db.ProliferationGranularEntries.AsNoTracking()
                        join project in _db.Projects.AsNoTracking() on granular.ProjectId equals project.Id
                        join user in _db.Users.AsNoTracking() on granular.SubmittedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where granular.ApprovalStatus == ApprovalStatus.Pending
                        select new ProliferationGranularRow(
                            granular.Id,
                            granular.ProjectId,
                            project.Name,
                            granular.Source,
                            granular.UnitName,
                            granular.ProliferationDate,
                            granular.Quantity,
                            granular.SubmittedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            granular.CreatedOnUtc,
                            granular.RowVersion);

        baseQuery = ApplySearch(baseQuery, query);
        return baseQuery;
    }

    private static ApprovalQueueItemVm MapProliferationGranularRow(ProliferationGranularRow row)
    {
        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} proliferation on {1:dd MMM yyyy} ({2})",
            row.Source.ToDisplayName(),
            row.ProliferationDate,
            row.UnitName);

        return new ApprovalQueueItemVm(
            ApprovalQueueType.ProliferationGranular,
            row.Id.ToString(),
            row.ProjectId,
            row.ProjectName,
            row.SubmittedByUserId,
            ResolveUserDisplayName(row.SubmittedByFullName, row.SubmittedByUserName, row.SubmittedByEmail),
            row.CreatedOnUtc,
            summary,
            ApprovalQueueModule.ProjectOfficeReports,
            ApprovalStatus.Pending.ToString(),
            null,
            Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()));
    }

    // SECTION: Activity delete list
    private IQueryable<ActivityDeleteRow> BuildActivityDeleteQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from request in _db.ActivityDeleteRequests.AsNoTracking()
                        join activity in _db.Activities.AsNoTracking() on request.ActivityId equals activity.Id
                        join activityType in _db.ActivityTypes.AsNoTracking() on activity.ActivityTypeId equals activityType.Id
                        join user in _db.Users.AsNoTracking() on request.RequestedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where request.ApprovedAtUtc == null
                              && request.RejectedAtUtc == null
                              && !activity.IsDeleted
                        select new ActivityDeleteRow(
                            request.Id,
                            activity.Id,
                            activity.Title,
                            activityType.Name,
                            activity.Location,
                            activity.ScheduledStartUtc,
                            request.RequestedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            request.RequestedAtUtc,
                            request.Reason,
                            request.RowVersion);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = $"%{query.Search.Trim()}%";
            baseQuery = baseQuery.Where(row =>
                EF.Functions.ILike(row.ActivityTitle, like)
                || EF.Functions.ILike(row.ActivityTypeName, like)
                || EF.Functions.ILike(row.RequestedByFullName ?? string.Empty, like)
                || EF.Functions.ILike(row.RequestedByUserName ?? string.Empty, like));
        }

        return baseQuery;
    }

    private static ApprovalQueueItemVm MapActivityDeleteRow(ActivityDeleteRow row)
        => new(
            ApprovalQueueType.ActivityDelete,
            row.Id.ToString(CultureInfo.InvariantCulture),
            null,
            null,
            row.RequestedByUserId,
            ResolveUserDisplayName(row.RequestedByFullName, row.RequestedByUserName, row.RequestedByEmail),
            row.RequestedAtUtc,
            $"Delete activity: {row.ActivityTitle}",
            ApprovalQueueModule.Activities,
            PendingDecisionStatus,
            null,
            Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()));

    // SECTION: Training delete list
    private IQueryable<TrainingDeleteRow> BuildTrainingDeleteQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from request in _db.TrainingDeleteRequests.AsNoTracking()
                        join training in _db.Trainings.AsNoTracking() on request.TrainingId equals training.Id
                        join trainingType in _db.TrainingTypes.AsNoTracking() on training.TrainingTypeId equals trainingType.Id
                        join counter in _db.TrainingCounters.AsNoTracking() on training.Id equals counter.TrainingId into counterGroup
                        from counter in counterGroup.DefaultIfEmpty()
                        join user in _db.Users.AsNoTracking() on request.RequestedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where request.Status == TrainingDeleteRequestStatus.Pending
                        select new TrainingDeleteRow(
                            request.Id,
                            training.Id,
                            trainingType.Name,
                            training.StartDate,
                            training.EndDate,
                            training.TrainingMonth,
                            training.TrainingYear,
                            counter != null ? counter.Total : training.LegacyOfficerCount + training.LegacyJcoCount + training.LegacyOrCount,
                            request.RequestedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            request.RequestedAtUtc,
                            request.Reason,
                            request.RowVersion);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = $"%{query.Search.Trim()}%";
            baseQuery = baseQuery.Where(row =>
                EF.Functions.ILike(row.TrainingTypeName, like)
                || EF.Functions.ILike(row.RequestedByFullName ?? string.Empty, like)
                || EF.Functions.ILike(row.RequestedByUserName ?? string.Empty, like));
        }

        return baseQuery;
    }

    private static ApprovalQueueItemVm MapTrainingDeleteRow(TrainingDeleteRow row)
        => new(
            ApprovalQueueType.TrainingDelete,
            row.Id.ToString(),
            null,
            null,
            row.RequestedByUserId,
            ResolveUserDisplayName(row.RequestedByFullName, row.RequestedByUserName, row.RequestedByEmail),
            row.RequestedAtUtc,
            $"Delete training record: {row.TrainingTypeName}",
            ApprovalQueueModule.ProjectOfficeReports,
            PendingDecisionStatus,
            null,
            Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()));

    // SECTION: Repository document delete list
    private IQueryable<RepositoryDocumentDeleteRow> BuildRepositoryDocumentDeleteQuery(ApprovalQueueQuery query)
    {
        var baseQuery = from request in _db.DocumentDeleteRequests.AsNoTracking()
                        join document in _db.Documents.AsNoTracking() on request.DocumentId equals document.Id
                        join user in _db.Users.AsNoTracking() on request.RequestedByUserId equals user.Id into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where request.ApprovedAtUtc == null && !document.IsDeleted
                        select new RepositoryDocumentDeleteRow(
                            request.Id,
                            document.Id,
                            document.Subject,
                            document.ReceivedFrom,
                            document.DocumentDate,
                            document.OriginalFileName,
                            document.FileSizeBytes,
                            request.RequestedByUserId,
                            user.FullName,
                            user.UserName,
                            user.Email,
                            request.RequestedAtUtc,
                            request.Reason);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = $"%{query.Search.Trim()}%";
            baseQuery = baseQuery.Where(row =>
                EF.Functions.ILike(row.Subject, like)
                || EF.Functions.ILike(row.OriginalFileName, like)
                || EF.Functions.ILike(row.ReceivedFrom ?? string.Empty, like)
                || EF.Functions.ILike(row.RequestedByFullName ?? string.Empty, like)
                || EF.Functions.ILike(row.RequestedByUserName ?? string.Empty, like));
        }

        return baseQuery;
    }

    private static ApprovalQueueItemVm MapRepositoryDocumentDeleteRow(RepositoryDocumentDeleteRow row)
        => new(
            ApprovalQueueType.RepositoryDocumentDelete,
            row.Id.ToString(CultureInfo.InvariantCulture),
            null,
            null,
            row.RequestedByUserId,
            ResolveUserDisplayName(row.RequestedByFullName, row.RequestedByUserName, row.RequestedByEmail),
            row.RequestedAtUtc,
            $"Move repository document to trash: {row.Subject}",
            ApprovalQueueModule.DocumentRepository,
            PendingDecisionStatus,
            null,
            null);

    private static ApprovalQueueItemVm ApplyStageAssessment(
        ApprovalQueueItemVm item,
        StageApprovalAssessment assessment)
        => item with
        {
            Readiness = assessment.Readiness,
            ReadinessMessage = assessment.Message,
            WorkflowVersion = assessment.WorkflowVersion,
            StageCode = assessment.StageCode,
            WorkflowOrder = assessment.WorkflowOrder,
            RevisionNumber = assessment.RevisionNumber,
            CorrectionUrl = assessment.CorrectionUrl
        };

    private static int ReadinessOrder(ApprovalReadiness readiness)
        => readiness switch
        {
            ApprovalReadiness.Ready => 0,
            ApprovalReadiness.Waiting => 1,
            ApprovalReadiness.Blocked => 2,
            ApprovalReadiness.Stale => 3,
            ApprovalReadiness.Superseded => 4,
            _ => 5
        };

    // SECTION: Stage change detail
    private async Task<ApprovalQueueDetailVm?> BuildStageChangeDetailAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return null;
        }

        var request = await _db.StageChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return null;
        }

        var stage = await _db.ProjectStages
            .AsNoTracking()
            .Include(s => s.Project)
            .FirstOrDefaultAsync(s => s.ProjectId == request.ProjectId && s.StageCode == request.StageCode, ct);

        if (stage?.Project is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(request.RequestedByUserId, ct);
        var stageName = StageCodes.DisplayNameOf(stage.Project.WorkflowVersion, stage.StageCode);
        var assessment = await _stageSequence.AssessRequestAsync(request.Id, ct);

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.StageChange,
            request.Id.ToString(CultureInfo.InvariantCulture),
            stage.ProjectId,
            stage.Project.Name,
            request.RequestedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            request.RequestedOn,
            $"{stageName} stage update request.",
            ApprovalQueueModule.Projects,
            request.DecisionStatus,
            null,
            null);

        if (assessment is not null)
        {
            item = ApplyStageAssessment(item, assessment);
        }

        var detail = new StageChangeDetailVm(
            request.StageCode,
            stageName,
            assessment?.WorkflowVersion ?? stage.Project.WorkflowVersion ?? PlanConstants.DefaultStageTemplateVersion,
            assessment?.WorkflowOrder ?? int.MaxValue,
            assessment?.RevisionNumber ?? 1,
            assessment?.IsLatestRevision ?? true,
            stage.Status.ToString(),
            request.RequestedStatus,
            stage.ActualStart,
            stage.CompletedOn,
            request.RequestedDate,
            request.Note);

        var relatedRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == request.ProjectId
                && r.Id != request.Id
                && (r.DecisionStatus == PendingDecisionStatus || r.StageCode == request.StageCode))
            .OrderBy(r => r.RequestedOn)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        var relatedAssessments = await _stageSequence.AssessPendingAsync(
            new[] { request.ProjectId },
            ct);

        var related = relatedRequests
            .Select(r =>
            {
                relatedAssessments.TryGetValue(r.Id, out var relatedAssessment);
                var relatedStageName = StageCodes.DisplayNameOf(stage.Project.WorkflowVersion, r.StageCode);
                var readiness = string.Equals(r.DecisionStatus, PendingDecisionStatus, StringComparison.OrdinalIgnoreCase)
                    ? relatedAssessment?.Readiness ?? ApprovalReadiness.Stale
                    : string.Equals(r.DecisionStatus, "Superseded", StringComparison.OrdinalIgnoreCase)
                        ? ApprovalReadiness.Superseded
                        : ApprovalReadiness.Stale;
                return new RelatedApprovalVm(
                    ApprovalQueueType.StageChange,
                    r.Id.ToString(CultureInfo.InvariantCulture),
                    relatedStageName,
                    $"{relatedStageName} to {FormatStatusLabel(r.RequestedStatus)}",
                    r.DecisionStatus,
                    readiness,
                    r.RequestedOn,
                    $"/Approvals/Pending/{ApprovalQueueType.StageChange}/{r.Id}");
            })
            .OrderBy(r => relatedAssessments.TryGetValue(int.Parse(r.RequestId, CultureInfo.InvariantCulture), out var a)
                ? a.WorkflowOrder
                : string.Equals(r.Label, stageName, StringComparison.OrdinalIgnoreCase)
                    ? assessment?.WorkflowOrder ?? int.MaxValue
                    : int.MaxValue)
            .ThenBy(r => r.RequestedAtUtc)
            .ToList();

        return new ApprovalQueueDetailVm
        {
            Item = item,
            StageChange = detail,
            ReadinessChecks = assessment?.Checks ?? Array.Empty<ApprovalCheckVm>(),
            RelatedRequests = related
        };
    }

    // SECTION: Meta change detail
    private async Task<ApprovalQueueDetailVm?> BuildMetaChangeDetailAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return null;
        }

        var request = await _db.ProjectMetaChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.TechnicalCategory)
            .Include(p => p.SponsoringUnit)
            .Include(p => p.SponsoringLineDirectorate)
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);

        if (project is null)
        {
            return null;
        }

        var metaVm = await ProjectMetaChangeRequestReader.BuildAsync(_db, request, project, ct);
        if (metaVm is null)
        {
            return null;
        }

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.ProjectMeta,
            request.Id.ToString(CultureInfo.InvariantCulture),
            project.Id,
            project.Name,
            request.RequestedByUserId ?? string.Empty,
            metaVm.RequestedBy ?? "Unknown",
            request.RequestedOnUtc,
            metaVm.Summary,
            ApprovalQueueModule.Projects,
            request.DecisionStatus,
            null,
            null);

        return new ApprovalQueueDetailVm
        {
            Item = item,
            MetaChange = metaVm
        };
    }

    // SECTION: Plan approval detail
    private async Task<ApprovalQueueDetailVm?> BuildPlanApprovalDetailAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return null;
        }

        var plan = await _db.PlanVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (plan is null)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == plan.ProjectId, ct);

        if (project is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(plan.SubmittedByUserId, ct);
        var diffs = await _planCompareService.GetDraftVsCurrentAsync(plan.ProjectId, ct);
        var diffVms = diffs.Select(diff => new PlanStageDiffVm(
                diff.StageCode,
                StageCodes.DisplayNameOf(project.WorkflowVersion, diff.StageCode),
                diff.OldStart,
                diff.OldDue,
                diff.NewStart,
                diff.NewDue))
            .ToList();

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.PlanApproval,
            plan.Id.ToString(CultureInfo.InvariantCulture),
            project.Id,
            project.Name,
            plan.SubmittedByUserId ?? string.Empty,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            plan.SubmittedOn ?? DateTimeOffset.MinValue,
            $"Plan version {plan.VersionNo} awaiting approval.",
            ApprovalQueueModule.Projects,
            plan.Status.ToString(),
            null,
            null);

        var detail = new PlanApprovalDetailVm(
            plan.Id,
            plan.VersionNo,
            diffVms,
            plan.Status,
            plan.SubmittedOn,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email));

        return new ApprovalQueueDetailVm
        {
            Item = item,
            PlanApproval = detail
        };
    }

    // SECTION: Document moderation detail
    private async Task<ApprovalQueueDetailVm?> BuildDocumentModerationDetailAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return null;
        }

        var request = await _db.ProjectDocumentRequests
            .AsNoTracking()
            .Include(r => r.Stage)
            .Include(r => r.Document)
            .Include(r => r.Document!.UploadedByUser)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);

        if (project is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(request.RequestedByUserId, ct);

        DocumentSummaryVm? summary = null;
        if (request.Document is not null)
        {
            var uploadedBy = request.Document.UploadedByUser;
            var uploaderName = uploadedBy is null
                ? "Unknown"
                : ResolveUserDisplayName(uploadedBy.FullName, uploadedBy.UserName, uploadedBy.Email);

            summary = new DocumentSummaryVm(
                request.Document.Id,
                request.Document.Title,
                request.Document.OriginalFileName,
                request.Document.FileSize,
                request.Document.FileStamp,
                request.Document.UploadedAtUtc,
                uploaderName,
                request.Document.IsArchived);
        }

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.DocRequest,
            request.Id.ToString(CultureInfo.InvariantCulture),
            project.Id,
            project.Name,
            request.RequestedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            request.RequestedAtUtc,
            $"Document moderation request for {request.Title}.",
            ApprovalQueueModule.Projects,
            request.Status.ToString(),
            null,
            null);

        var detail = new DocumentModerationDetailVm(
            request.Id,
            request.RequestType,
            request.Title,
            request.Description,
            request.Stage is null ? "General" : StageCodes.DisplayNameOf(project.WorkflowVersion, request.Stage.StageCode),
            request.Stage?.StageCode,
            request.OriginalFileName,
            request.ContentType,
            request.FileSize,
            summary,
            summary is not null ? $"/Projects/Documents/Preview?documentId={summary.DocumentId}" : null,
            summary is not null ? $"/Projects/Documents/Preview?documentId={summary.DocumentId}" : null);

        return new ApprovalQueueDetailVm
        {
            Item = item,
            DocumentModeration = detail
        };
    }

    // SECTION: ToT request detail
    private async Task<ApprovalQueueDetailVm?> BuildTotRequestDetailAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return null;
        }

        var request = await _db.ProjectTotRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Include(p => p.Tot)
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);

        if (project?.Tot is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(request.SubmittedByUserId, ct);

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.TotRequest,
            request.Id.ToString(CultureInfo.InvariantCulture),
            project.Id,
            project.Name,
            request.SubmittedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            request.SubmittedOnUtc,
            $"Transfer of Technology status update to {request.ProposedStatus}.",
            ApprovalQueueModule.ProjectOfficeReports,
            request.DecisionState.ToString(),
            null,
            Convert.ToBase64String(request.RowVersion ?? Array.Empty<byte>()));

        var detail = new TotRequestDetailVm(
            project.Tot.Status,
            request.ProposedStatus,
            project.Tot.StartedOn,
            request.ProposedStartedOn,
            project.Tot.CompletedOn,
            request.ProposedCompletedOn,
            project.Tot.MetDetails,
            request.ProposedMetDetails,
            project.Tot.MetCompletedOn,
            request.ProposedMetCompletedOn,
            project.Tot.FirstProductionModelManufactured,
            request.ProposedFirstProductionModelManufactured,
            project.Tot.FirstProductionModelManufacturedOn,
            request.ProposedFirstProductionModelManufacturedOn);

        return new ApprovalQueueDetailVm
        {
            Item = item,
            TotRequest = detail
        };
    }

    // SECTION: Proliferation yearly detail
    private async Task<ApprovalQueueDetailVm?> BuildProliferationYearlyDetailAsync(string requestId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestId, out var id))
        {
            return null;
        }

        var record = await _db.ProliferationYearlies
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == id, ct);

        if (record is null)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == record.ProjectId, ct);

        if (project is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(record.SubmittedByUserId, ct);

        var previous = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(y =>
                y.ProjectId == record.ProjectId &&
                y.Source == record.Source &&
                y.Year == record.Year &&
                y.ApprovalStatus == ApprovalStatus.Approved &&
                y.Id != record.Id)
            .Select(y => new ProliferationYearlySnapshotVm(
                y.TotalQuantity,
                y.Remarks,
                y.ApprovedOnUtc))
            .FirstOrDefaultAsync(ct);

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.ProliferationYearly,
            record.Id.ToString(),
            record.ProjectId,
            project.Name,
            record.SubmittedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            record.CreatedOnUtc,
            $"{record.Source.ToDisplayName()} yearly proliferation {record.Year}.",
            ApprovalQueueModule.ProjectOfficeReports,
            record.ApprovalStatus.ToString(),
            null,
            Convert.ToBase64String(record.RowVersion ?? Array.Empty<byte>()));

        var detail = new ProliferationYearlyDetailVm(
            record.Id,
            record.Source.ToDisplayName(),
            record.Year,
            record.TotalQuantity,
            record.Remarks,
            previous);

        return new ApprovalQueueDetailVm
        {
            Item = item,
            ProliferationYearly = detail
        };
    }

    // SECTION: Proliferation granular detail
    private async Task<ApprovalQueueDetailVm?> BuildProliferationGranularDetailAsync(string requestId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestId, out var id))
        {
            return null;
        }

        var record = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (record is null)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == record.ProjectId, ct);

        if (project is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(record.SubmittedByUserId, ct);

        var previous = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g =>
                g.ProjectId == record.ProjectId &&
                g.Source == record.Source &&
                g.ProliferationDate == record.ProliferationDate &&
                g.UnitName == record.UnitName &&
                g.ApprovalStatus == ApprovalStatus.Approved &&
                g.Id != record.Id)
            .Select(g => new ProliferationGranularSnapshotVm(
                g.Quantity,
                g.Remarks,
                g.ApprovedOnUtc))
            .FirstOrDefaultAsync(ct);

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.ProliferationGranular,
            record.Id.ToString(),
            record.ProjectId,
            project.Name,
            record.SubmittedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            record.CreatedOnUtc,
            $"{record.Source.ToDisplayName()} proliferation on {record.ProliferationDate:dd MMM yyyy}.",
            ApprovalQueueModule.ProjectOfficeReports,
            record.ApprovalStatus.ToString(),
            null,
            Convert.ToBase64String(record.RowVersion ?? Array.Empty<byte>()));

        var detail = new ProliferationGranularDetailVm(
            record.Id,
            record.Source.ToDisplayName(),
            record.UnitName,
            record.ProliferationDate,
            record.Quantity,
            record.Remarks,
            previous);

        return new ApprovalQueueDetailVm
        {
            Item = item,
            ProliferationGranular = detail
        };
    }

    // SECTION: Activity delete detail
    private async Task<ApprovalQueueDetailVm?> BuildActivityDeleteDetailAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return null;
        }

        var request = await _db.ActivityDeleteRequests
            .AsNoTracking()
            .Include(item => item.Activity)
                .ThenInclude(activity => activity.ActivityType)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        if (request?.Activity is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(request.RequestedByUserId, ct);
        var status = request.ApprovedAtUtc.HasValue
            ? "Approved"
            : request.RejectedAtUtc.HasValue
                ? "Rejected"
                : PendingDecisionStatus;
        var readiness = !request.ApprovedAtUtc.HasValue && !request.RejectedAtUtc.HasValue && !request.Activity.IsDeleted
            ? ApprovalReadiness.Ready
            : ApprovalReadiness.Stale;

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.ActivityDelete,
            request.Id.ToString(CultureInfo.InvariantCulture),
            null,
            null,
            request.RequestedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            request.RequestedAtUtc,
            $"Delete activity: {request.Activity.Title}",
            ApprovalQueueModule.Activities,
            status,
            null,
            Convert.ToBase64String(request.RowVersion ?? Array.Empty<byte>()),
            readiness,
            readiness == ApprovalReadiness.Ready ? "Ready for decision." : "This request is no longer actionable.");

        return new ApprovalQueueDetailVm
        {
            Item = item,
            ReadinessChecks = new[]
            {
                new ApprovalCheckVm(
                    readiness == ApprovalReadiness.Ready ? ApprovalCheckState.Passed : ApprovalCheckState.Blocked,
                    "Activity availability",
                    readiness == ApprovalReadiness.Ready
                        ? "The activity is available and the delete request is pending."
                        : "The activity or delete request is no longer available.")
            },
            ActivityDelete = new ActivityDeleteDetailVm(
                request.ActivityId,
                request.Activity.Title,
                request.Activity.ActivityType?.Name ?? "Activity",
                request.Activity.Location,
                request.Activity.ScheduledStartUtc,
                request.Reason)
        };
    }

    // SECTION: Training delete detail
    private async Task<ApprovalQueueDetailVm?> BuildTrainingDeleteDetailAsync(string requestId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestId, out var id))
        {
            return null;
        }

        var request = await _db.TrainingDeleteRequests
            .AsNoTracking()
            .Include(item => item.Training)
                .ThenInclude(training => training!.TrainingType)
            .Include(item => item.Training)
                .ThenInclude(training => training!.Counters)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        if (request?.Training is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(request.RequestedByUserId, ct);
        var readiness = request.Status == TrainingDeleteRequestStatus.Pending
            ? ApprovalReadiness.Ready
            : ApprovalReadiness.Stale;
        var total = request.Training.Counters?.Total
            ?? request.Training.LegacyOfficerCount + request.Training.LegacyJcoCount + request.Training.LegacyOrCount;

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.TrainingDelete,
            request.Id.ToString(),
            null,
            null,
            request.RequestedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            request.RequestedAtUtc,
            $"Delete training record: {request.Training.TrainingType?.Name ?? "Training"}",
            ApprovalQueueModule.ProjectOfficeReports,
            request.Status.ToString(),
            null,
            Convert.ToBase64String(request.RowVersion ?? Array.Empty<byte>()),
            readiness,
            readiness == ApprovalReadiness.Ready ? "Ready for decision." : "This request is no longer pending.");

        return new ApprovalQueueDetailVm
        {
            Item = item,
            ReadinessChecks = new[]
            {
                new ApprovalCheckVm(
                    readiness == ApprovalReadiness.Ready ? ApprovalCheckState.Passed : ApprovalCheckState.Blocked,
                    "Training availability",
                    readiness == ApprovalReadiness.Ready
                        ? "The training record is available and the request is pending."
                        : "The training delete request is no longer pending.")
            },
            TrainingDelete = new TrainingDeleteDetailVm(
                request.TrainingId,
                request.Training.TrainingType?.Name ?? "Training",
                FormatTrainingPeriod(
                    request.Training.StartDate,
                    request.Training.EndDate,
                    request.Training.TrainingMonth,
                    request.Training.TrainingYear),
                total,
                request.Reason)
        };
    }

    // SECTION: Repository document delete detail
    private async Task<ApprovalQueueDetailVm?> BuildRepositoryDocumentDeleteDetailAsync(string requestId, CancellationToken ct)
    {
        if (!long.TryParse(requestId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var request = await _db.DocumentDeleteRequests
            .AsNoTracking()
            .Include(item => item.Document)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        if (request?.Document is null)
        {
            return null;
        }

        var requester = await GetUserSnapshotAsync(request.RequestedByUserId, ct);
        var readiness = !request.ApprovedAtUtc.HasValue && !request.Document.IsDeleted
            ? ApprovalReadiness.Ready
            : ApprovalReadiness.Stale;
        var status = request.ApprovedAtUtc.HasValue ? "Approved" : PendingDecisionStatus;

        var item = new ApprovalQueueItemVm(
            ApprovalQueueType.RepositoryDocumentDelete,
            request.Id.ToString(CultureInfo.InvariantCulture),
            null,
            null,
            request.RequestedByUserId,
            ResolveUserDisplayName(requester.FullName, requester.UserName, requester.Email),
            request.RequestedAtUtc,
            $"Move repository document to trash: {request.Document.Subject}",
            ApprovalQueueModule.DocumentRepository,
            status,
            null,
            null,
            readiness,
            readiness == ApprovalReadiness.Ready ? "Ready for decision." : "The document is no longer available.");

        return new ApprovalQueueDetailVm
        {
            Item = item,
            ReadinessChecks = new[]
            {
                new ApprovalCheckVm(
                    readiness == ApprovalReadiness.Ready ? ApprovalCheckState.Passed : ApprovalCheckState.Blocked,
                    "Document availability",
                    readiness == ApprovalReadiness.Ready
                        ? "The repository document is available and the request is pending."
                        : "The repository document or request is no longer available.")
            },
            RepositoryDocumentDelete = new RepositoryDocumentDeleteDetailVm(
                request.DocumentId,
                request.Document.Subject,
                request.Document.ReceivedFrom,
                request.Document.DocumentDate,
                request.Document.OriginalFileName,
                request.Document.FileSizeBytes,
                request.Reason)
        };
    }

    // SECTION: Query helpers
    private static IQueryable<T> ApplySearch<T>(IQueryable<T> queryable, ApprovalQueueQuery query) where T : class
    {
        if (string.IsNullOrWhiteSpace(query.Search))
        {
            return queryable;
        }

        var trimmed = query.Search.Trim();
        var normalized = string.Concat(trimmed.Where(ch => !char.IsWhiteSpace(ch)));
        var like = $"%{trimmed}%";
        var parsedInt = int.TryParse(trimmed, out var intId) ? intId : (int?)null;
        var parsedGuid = Guid.TryParse(trimmed, out var guidId) ? guidId : (Guid?)null;
        var parsedDocRequestType = Enum.TryParse<ProjectDocumentRequestType>(normalized, true, out var docRequestType)
            ? docRequestType
            : (ProjectDocumentRequestType?)null;
        var parsedTotStatus = Enum.TryParse<ProjectTotStatus>(normalized, true, out var totStatus)
            ? totStatus
            : (ProjectTotStatus?)null;
        var parsedSource = Enum.TryParse<ProliferationSource>(normalized, true, out var source)
            ? source
            : (ProliferationSource?)null;

        if (queryable is IQueryable<StageChangeRow> stage)
        {
            var filtered = stage.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.RequestedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.RequestedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.RequestedByEmail ?? string.Empty, like) ||
                EF.Functions.ILike(r.StageCode, like) ||
                EF.Functions.ILike(r.RequestedStatus, like) ||
                (parsedInt.HasValue && r.Id == parsedInt.Value));
            return (IQueryable<T>)(object)filtered;
        }

        if (queryable is IQueryable<MetaChangeRow> meta)
        {
            var filtered = meta.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.RequestedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.RequestedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.RequestedByEmail ?? string.Empty, like) ||
                (parsedInt.HasValue && r.Id == parsedInt.Value));
            return (IQueryable<T>)(object)filtered;
        }

        if (queryable is IQueryable<PlanApprovalRow> plan)
        {
            var filtered = plan.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.SubmittedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByEmail ?? string.Empty, like) ||
                (parsedInt.HasValue && r.VersionNo == parsedInt.Value) ||
                (parsedInt.HasValue && r.Id == parsedInt.Value));
            return (IQueryable<T>)(object)filtered;
        }

        if (queryable is IQueryable<DocumentRequestRow> doc)
        {
            var filtered = doc.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.Title, like) ||
                EF.Functions.ILike(r.RequestedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.RequestedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.RequestedByEmail ?? string.Empty, like) ||
                (parsedDocRequestType.HasValue && r.RequestType == parsedDocRequestType.Value) ||
                (parsedInt.HasValue && r.Id == parsedInt.Value));
            return (IQueryable<T>)(object)filtered;
        }

        if (queryable is IQueryable<TotRequestRow> tot)
        {
            var filtered = tot.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.SubmittedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByEmail ?? string.Empty, like) ||
                (parsedTotStatus.HasValue && r.ProposedStatus == parsedTotStatus.Value) ||
                (parsedInt.HasValue && r.Id == parsedInt.Value));
            return (IQueryable<T>)(object)filtered;
        }

        if (queryable is IQueryable<ProliferationYearlyRow> yearly)
        {
            var filtered = yearly.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.SubmittedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByEmail ?? string.Empty, like) ||
                (parsedSource.HasValue && r.Source == parsedSource.Value) ||
                (parsedInt.HasValue && r.Year == parsedInt.Value) ||
                (parsedInt.HasValue && r.TotalQuantity == parsedInt.Value) ||
                (parsedGuid.HasValue && r.Id == parsedGuid.Value));
            return (IQueryable<T>)(object)filtered;
        }

        if (queryable is IQueryable<ProliferationGranularRow> granular)
        {
            var filtered = granular.Where(r =>
                EF.Functions.ILike(r.ProjectName, like) ||
                EF.Functions.ILike(r.UnitName, like) ||
                EF.Functions.ILike(r.SubmittedByFullName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByUserName ?? string.Empty, like) ||
                EF.Functions.ILike(r.SubmittedByEmail ?? string.Empty, like) ||
                (parsedSource.HasValue && r.Source == parsedSource.Value) ||
                (parsedGuid.HasValue && r.Id == parsedGuid.Value));
            return (IQueryable<T>)(object)filtered;
        }

        return queryable;
    }

    private static bool ShouldIncludeType(ApprovalQueueQuery query, ApprovalQueueType type)
    {
        if (query.Type.HasValue && query.Type.Value != type)
        {
            return false;
        }

        return !query.Module.HasValue || ModuleFor(type) == query.Module.Value;
    }

    private static ApprovalQueueModule ModuleFor(ApprovalQueueType type)
        => type switch
        {
            ApprovalQueueType.StageChange or
            ApprovalQueueType.ProjectMeta or
            ApprovalQueueType.PlanApproval or
            ApprovalQueueType.DocRequest => ApprovalQueueModule.Projects,

            ApprovalQueueType.TotRequest or
            ApprovalQueueType.ProliferationYearly or
            ApprovalQueueType.ProliferationGranular or
            ApprovalQueueType.TrainingDelete => ApprovalQueueModule.ProjectOfficeReports,

            ApprovalQueueType.ActivityDelete => ApprovalQueueModule.Activities,
            ApprovalQueueType.RepositoryDocumentDelete => ApprovalQueueModule.DocumentRepository,
            _ => ApprovalQueueModule.Projects
        };

    private static string ResolveUserDisplayName(string? fullName, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        return !string.IsNullOrWhiteSpace(email) ? email : "Unknown";
    }

    private async Task<UserSnapshot> GetUserSnapshotAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new UserSnapshot(null, null, null);
        }

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSnapshot(u.FullName, u.UserName, u.Email))
            .FirstOrDefaultAsync(ct);

        return user ?? new UserSnapshot(null, null, null);
    }

    private static string FormatStatusLabel(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "unknown";
        }

        return status switch
        {
            "NotStarted" => "not started",
            "InProgress" => "in progress",
            _ => status
        };
    }

    private static string FormatTrainingPeriod(
        DateOnly? startDate,
        DateOnly? endDate,
        int? trainingMonth,
        int? trainingYear)
    {
        if (startDate.HasValue && endDate.HasValue)
        {
            return startDate.Value == endDate.Value
                ? startDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                : $"{startDate.Value:dd MMM yyyy} – {endDate.Value:dd MMM yyyy}";
        }

        if (startDate.HasValue)
        {
            return startDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        if (trainingMonth.HasValue && trainingYear.HasValue
            && trainingMonth.Value is >= 1 and <= 12)
        {
            return new DateTime(trainingYear.Value, trainingMonth.Value, 1)
                .ToString("MMM yyyy", CultureInfo.InvariantCulture);
        }

        return trainingYear?.ToString(CultureInfo.InvariantCulture) ?? "Period not recorded";
    }

    // SECTION: Query row models
    private sealed record StageChangeRow(
        int Id,
        int ProjectId,
        string ProjectName,
        string? WorkflowVersion,
        string StageCode,
        string RequestedStatus,
        DateOnly? RequestedDate,
        string RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTimeOffset RequestedOn);

    private sealed record MetaChangeRow(
        int Id,
        int ProjectId,
        string ProjectName,
        string RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTimeOffset RequestedOnUtc);

    private sealed record PlanApprovalRow(
        int Id,
        int ProjectId,
        string ProjectName,
        int VersionNo,
        string SubmittedByUserId,
        string? SubmittedByFullName,
        string? SubmittedByUserName,
        string? SubmittedByEmail,
        DateTimeOffset SubmittedOnUtc);

    private sealed record DocumentRequestRow(
        int Id,
        int ProjectId,
        string ProjectName,
        ProjectDocumentRequestType RequestType,
        string Title,
        string RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTimeOffset RequestedAtUtc);

    private sealed record TotRequestRow(
        int Id,
        int ProjectId,
        string ProjectName,
        ProjectTotStatus ProposedStatus,
        string SubmittedByUserId,
        string? SubmittedByFullName,
        string? SubmittedByUserName,
        string? SubmittedByEmail,
        DateTimeOffset SubmittedOnUtc,
        byte[] RowVersion);

    private sealed record ProliferationYearlyRow(
        Guid Id,
        int ProjectId,
        string ProjectName,
        ProliferationSource Source,
        int Year,
        int TotalQuantity,
        string SubmittedByUserId,
        string? SubmittedByFullName,
        string? SubmittedByUserName,
        string? SubmittedByEmail,
        DateTimeOffset CreatedOnUtc,
        byte[] RowVersion);

    private sealed record ProliferationGranularRow(
        Guid Id,
        int ProjectId,
        string ProjectName,
        ProliferationSource Source,
        string UnitName,
        DateOnly ProliferationDate,
        int Quantity,
        string SubmittedByUserId,
        string? SubmittedByFullName,
        string? SubmittedByUserName,
        string? SubmittedByEmail,
        DateTimeOffset CreatedOnUtc,
        byte[] RowVersion);

    private sealed record ActivityDeleteRow(
        int Id,
        int ActivityId,
        string ActivityTitle,
        string ActivityTypeName,
        string? ActivityLocation,
        DateTimeOffset? ScheduledStartUtc,
        string RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTimeOffset RequestedAtUtc,
        string? Reason,
        byte[] RowVersion);

    private sealed record TrainingDeleteRow(
        Guid Id,
        Guid TrainingId,
        string TrainingTypeName,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? TrainingMonth,
        int? TrainingYear,
        int TotalTrainees,
        string RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTimeOffset RequestedAtUtc,
        string Reason,
        byte[] RowVersion);

    private sealed record RepositoryDocumentDeleteRow(
        long Id,
        Guid DocumentId,
        string Subject,
        string? ReceivedFrom,
        DateOnly? DocumentDate,
        string OriginalFileName,
        long FileSizeBytes,
        string RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTimeOffset RequestedAtUtc,
        string? Reason);

    private sealed record UserSnapshot(string? FullName, string? UserName, string? Email);
}
