using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Services.Authorization;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Approvals;

public sealed class ApprovalDecisionService
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly StageDecisionService _stageDecisionService;
    private readonly ProjectMetaChangeDecisionService _metaDecisionService;
    private readonly PlanApprovalService _planApprovalService;
    private readonly IDocumentDecisionService _documentDecisionService;
    private readonly ProjectTotService _totService;
    private readonly ProliferationSubmissionService _proliferationService;
    private readonly ILogger<ApprovalDecisionService> _logger;

    public ApprovalDecisionService(
        ApplicationDbContext db,
        StageDecisionService stageDecisionService,
        ProjectMetaChangeDecisionService metaDecisionService,
        PlanApprovalService planApprovalService,
        IDocumentDecisionService documentDecisionService,
        ProjectTotService totService,
        ProliferationSubmissionService proliferationService,
        ILogger<ApprovalDecisionService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _stageDecisionService = stageDecisionService ?? throw new ArgumentNullException(nameof(stageDecisionService));
        _metaDecisionService = metaDecisionService ?? throw new ArgumentNullException(nameof(metaDecisionService));
        _planApprovalService = planApprovalService ?? throw new ArgumentNullException(nameof(planApprovalService));
        _documentDecisionService = documentDecisionService ?? throw new ArgumentNullException(nameof(documentDecisionService));
        _totService = totService ?? throw new ArgumentNullException(nameof(totService));
        _proliferationService = proliferationService ?? throw new ArgumentNullException(nameof(proliferationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // SECTION: Approval decision routing
    public async Task<ApprovalDecisionResult> DecideAsync(
        ApprovalDecisionRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ApprovalDecisionResult.Forbidden("User context is required.");
        }

        var isAdmin = user.IsInRole("Admin");
        var isHoD = user.IsInRole("HoD");
        if (!ApprovalAuthorization.CanApproveProjectChanges(isAdmin, isHoD))
        {
            return ApprovalDecisionResult.Forbidden("Only Admin or HoD users can approve pending requests.");
        }

        if (request.Decision == ApprovalDecisionAction.Reject && string.IsNullOrWhiteSpace(request.Remarks))
        {
            return ApprovalDecisionResult.ValidationFailed("A rejection reason is required.");
        }

        return request.ApprovalType switch
        {
            ApprovalQueueType.StageChange => await DecideStageChangeAsync(request, userId, isAdmin, isHoD, ct),
            ApprovalQueueType.ProjectMeta => await DecideMetaChangeAsync(request, userId, isAdmin, isHoD, ct),
            ApprovalQueueType.PlanApproval => await DecidePlanApprovalAsync(request, userId, isAdmin, isHoD, ct),
            ApprovalQueueType.DocRequest => await DecideDocumentRequestAsync(request, userId, isAdmin, isHoD, ct),
            ApprovalQueueType.TotRequest => await DecideTotRequestAsync(request, userId, isAdmin, isHoD, ct),
            ApprovalQueueType.ProliferationYearly => await DecideProliferationYearlyAsync(request, user, ct),
            ApprovalQueueType.ProliferationGranular => await DecideProliferationGranularAsync(request, user, ct),
            _ => ApprovalDecisionResult.ValidationFailed("Unsupported approval type.")
        };
    }

    // SECTION: Stage change decision
    private async Task<ApprovalDecisionResult> DecideStageChangeAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken ct)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid stage change request id.");
        }

        var input = new StageDecisionInput(id, request.Decision == ApprovalDecisionAction.Approve
            ? StageDecisionAction.Approve
            : StageDecisionAction.Reject, request.Remarks);

        var result = await _stageDecisionService.DecideAsync(input, userId, isAdmin, isHoD, ct);

        return result.Outcome switch
        {
            StageDecisionOutcome.Success => ApprovalDecisionResult.Success(),
            StageDecisionOutcome.NotHeadOfDepartment => ApprovalDecisionResult.Forbidden("Only Admin or HoD users can approve stage changes."),
            StageDecisionOutcome.RequestNotFound => ApprovalDecisionResult.NotFound("Request not found."),
            StageDecisionOutcome.StageNotFound => ApprovalDecisionResult.NotFound("Stage not found."),
            StageDecisionOutcome.AlreadyDecided => ApprovalDecisionResult.AlreadyDecided("This request has already been decided."),
            StageDecisionOutcome.ValidationFailed => ApprovalDecisionResult.ValidationFailed(result.Error ?? "Unable to process the stage change."),
            _ => ApprovalDecisionResult.Error("Unable to process the stage change.")
        };
    }

    // SECTION: Meta change decision
    private async Task<ApprovalDecisionResult> DecideMetaChangeAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken ct)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid metadata change request id.");
        }

        var decisionUser = new ProjectMetaDecisionUser(userId, isAdmin, isHoD);
        var decisionInput = new ProjectMetaDecisionInput(
            id,
            request.Decision == ApprovalDecisionAction.Approve ? ProjectMetaDecisionAction.Approve : ProjectMetaDecisionAction.Reject,
            request.Remarks);

        var result = await _metaDecisionService.DecideAsync(decisionInput, decisionUser, ct);

        return result.Outcome switch
        {
            ProjectMetaDecisionOutcome.Success => ApprovalDecisionResult.Success(),
            ProjectMetaDecisionOutcome.Forbidden => ApprovalDecisionResult.Forbidden("Only Admin or HoD users can approve metadata changes."),
            ProjectMetaDecisionOutcome.RequestNotFound => ApprovalDecisionResult.NotFound("Request not found."),
            ProjectMetaDecisionOutcome.AlreadyDecided => ApprovalDecisionResult.AlreadyDecided("This request has already been decided."),
            ProjectMetaDecisionOutcome.ValidationFailed => ApprovalDecisionResult.ValidationFailed(result.Error ?? "Unable to process the metadata change."),
            _ => ApprovalDecisionResult.Error("Unable to process the metadata change.")
        };
    }

    // SECTION: Plan approval decision
    private async Task<ApprovalDecisionResult> DecidePlanApprovalAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken ct)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid plan approval request id.");
        }

        var plan = await _db.PlanVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (plan is null)
        {
            return ApprovalDecisionResult.NotFound("Plan request not found.");
        }

        if (plan.Status != Models.Plans.PlanVersionStatus.PendingApproval)
        {
            return ApprovalDecisionResult.AlreadyDecided("This plan approval has already been decided.");
        }

        try
        {
            if (request.Decision == ApprovalDecisionAction.Approve)
            {
                var approved = await _planApprovalService.ApproveLatestDraftAsync(plan.ProjectId, userId, isAdmin, isHoD, ct);
                return approved
                    ? ApprovalDecisionResult.Success()
                    : ApprovalDecisionResult.AlreadyDecided("No pending plan was available to approve.");
            }

            var rejected = await _planApprovalService.RejectLatestPendingAsync(plan.ProjectId, userId, isAdmin, isHoD, request.Remarks, ct);
            return rejected
                ? ApprovalDecisionResult.Success()
                : ApprovalDecisionResult.AlreadyDecided("No pending plan was available to reject.");
        }
        catch (PlanApprovalValidationException ex)
        {
            var message = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
            return ApprovalDecisionResult.ValidationFailed(message);
        }
        catch (ForbiddenException ex)
        {
            return ApprovalDecisionResult.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plan approval decision failed for request {RequestId}.", request.RequestId);
            return ApprovalDecisionResult.Error("Unable to process the plan approval.");
        }
    }

    // SECTION: Document moderation decision
    private async Task<ApprovalDecisionResult> DecideDocumentRequestAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken ct)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid document request id.");
        }

        try
        {
            if (request.Decision == ApprovalDecisionAction.Approve)
            {
                await _documentDecisionService.ApproveAsync(id, userId, isAdmin, isHoD, request.Remarks, ct);
            }
            else
            {
                await _documentDecisionService.RejectAsync(id, userId, isAdmin, isHoD, request.Remarks, ct);
            }

            return ApprovalDecisionResult.Success();
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.Message;
            if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecisionResult.NotFound("Document request not found.");
            }

            if (message.Contains("Only submitted", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalDecisionResult.AlreadyDecided("This request has already been decided.");
            }

            return ApprovalDecisionResult.ValidationFailed(message);
        }
        catch (ForbiddenException ex)
        {
            return ApprovalDecisionResult.Forbidden(ex.Message);
        }
    }

    // SECTION: ToT decision
    private async Task<ApprovalDecisionResult> DecideTotRequestAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken ct)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid Transfer of Technology request id.");
        }

        byte[]? rowVersion = null;
        if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                return ApprovalDecisionResult.ValidationFailed("The request token is invalid. Refresh and try again.");
            }
        }

        var projectId = await _db.ProjectTotRequests
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.ProjectId)
            .FirstOrDefaultAsync(ct);

        if (projectId == 0)
        {
            return ApprovalDecisionResult.NotFound("Transfer of Technology request not found.");
        }

        var result = await _totService.DecideRequestAsync(
            projectId,
            request.Decision == ApprovalDecisionAction.Approve,
            userId,
            isAdmin,
            isHoD,
            rowVersion,
            ct);

        return result.Status switch
        {
            ProjectTotRequestActionStatus.Success => ApprovalDecisionResult.Success(),
            ProjectTotRequestActionStatus.NotFound => ApprovalDecisionResult.NotFound("Transfer of Technology request not found."),
            ProjectTotRequestActionStatus.Forbidden => ApprovalDecisionResult.Forbidden(result.ErrorMessage ?? "You are not authorised to approve Transfer of Technology updates."),
            ProjectTotRequestActionStatus.Conflict => ApprovalDecisionResult.AlreadyDecided(result.ErrorMessage ?? "The request has already been decided."),
            ProjectTotRequestActionStatus.ValidationFailed => ApprovalDecisionResult.ValidationFailed(result.ErrorMessage ?? "Unable to approve the request."),
            _ => ApprovalDecisionResult.Error("Unable to process the Transfer of Technology request.")
        };
    }

    // SECTION: Proliferation yearly decision
    private async Task<ApprovalDecisionResult> DecideProliferationYearlyAsync(
        ApprovalDecisionRequest request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid proliferation yearly request id.");
        }

        var status = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(y => y.Id == id)
            .Select(y => (int?)y.ApprovalStatus)
            .FirstOrDefaultAsync(ct);

        if (!status.HasValue)
        {
            return ApprovalDecisionResult.NotFound("Proliferation record not found.");
        }

        if (status.Value != (int)ApprovalStatus.Pending)
        {
            return ApprovalDecisionResult.AlreadyDecided("This proliferation request has already been decided.");
        }

        var result = await _proliferationService.DecideYearlyAsync(
            id,
            request.Decision == ApprovalDecisionAction.Approve,
            request.RowVersion,
            user,
            ct);

        return result.Success
            ? ApprovalDecisionResult.Success()
            : ApprovalDecisionResult.ValidationFailed(result.Error ?? "Unable to process the proliferation approval.");
    }

    // SECTION: Proliferation granular decision
    private async Task<ApprovalDecisionResult> DecideProliferationGranularAsync(
        ApprovalDecisionRequest request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid proliferation granular request id.");
        }

        var status = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => (int?)g.ApprovalStatus)
            .FirstOrDefaultAsync(ct);

        if (!status.HasValue)
        {
            return ApprovalDecisionResult.NotFound("Proliferation record not found.");
        }

        if (status.Value != (int)ApprovalStatus.Pending)
        {
            return ApprovalDecisionResult.AlreadyDecided("This proliferation request has already been decided.");
        }

        var result = await _proliferationService.DecideGranularAsync(
            id,
            request.Decision == ApprovalDecisionAction.Approve,
            request.RowVersion,
            user,
            ct);

        return result.Success
            ? ApprovalDecisionResult.Success()
            : ApprovalDecisionResult.ValidationFailed(result.Error ?? "Unable to process the proliferation approval.");
    }
}
