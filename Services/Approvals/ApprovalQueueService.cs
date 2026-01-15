using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

    public ApprovalQueueService(ApplicationDbContext db, PlanCompareService planCompareService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _planCompareService = planCompareService ?? throw new ArgumentNullException(nameof(planCompareService));
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
            results.AddRange(stageRows.Select(MapStageChangeRow));
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

        return results
            .OrderBy(item => item.RequestedAtUtc)
            .ToList();
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

        var detail = new StageChangeDetailVm(
            request.StageCode,
            stageName,
            stage.Status.ToString(),
            request.RequestedStatus,
            stage.ActualStart,
            stage.CompletedOn,
            request.RequestedDate,
            request.Note);

        return new ApprovalQueueDetailVm
        {
            Item = item,
            StageChange = detail
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

        return true;
    }

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

    private sealed record UserSnapshot(string? FullName, string? UserName, string? Email);
}
