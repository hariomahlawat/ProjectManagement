using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.Authorization;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Approvals;

/// <summary>
/// Routes central approval decisions to the owning domain service. Expected business
/// validation and concurrency failures are translated into controlled outcomes so the
/// Decision Centre never exposes an exception page for a normal decision conflict.
/// </summary>
public sealed class ApprovalDecisionService
{
    private readonly ApplicationDbContext _db;
    private readonly StageDecisionService _stageDecisionService;
    private readonly ProjectMetaChangeDecisionService _metaDecisionService;
    private readonly PlanApprovalService _planApprovalService;
    private readonly IDocumentDecisionService _documentDecisionService;
    private readonly ProjectTotService _totService;
    private readonly ProliferationSubmissionService _proliferationService;
    private readonly IActivityDeleteRequestService _activityDeleteService;
    private readonly TrainingWriteService _trainingWriteService;
    private readonly RepositoryDocumentDeleteApprovalService _repositoryDeleteService;
    private readonly ILogger<ApprovalDecisionService> _logger;

    public ApprovalDecisionService(
        ApplicationDbContext db,
        StageDecisionService stageDecisionService,
        ProjectMetaChangeDecisionService metaDecisionService,
        PlanApprovalService planApprovalService,
        IDocumentDecisionService documentDecisionService,
        ProjectTotService totService,
        ProliferationSubmissionService proliferationService,
        IActivityDeleteRequestService activityDeleteService,
        TrainingWriteService trainingWriteService,
        RepositoryDocumentDeleteApprovalService repositoryDeleteService,
        ILogger<ApprovalDecisionService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _stageDecisionService = stageDecisionService ?? throw new ArgumentNullException(nameof(stageDecisionService));
        _metaDecisionService = metaDecisionService ?? throw new ArgumentNullException(nameof(metaDecisionService));
        _planApprovalService = planApprovalService ?? throw new ArgumentNullException(nameof(planApprovalService));
        _documentDecisionService = documentDecisionService ?? throw new ArgumentNullException(nameof(documentDecisionService));
        _totService = totService ?? throw new ArgumentNullException(nameof(totService));
        _proliferationService = proliferationService ?? throw new ArgumentNullException(nameof(proliferationService));
        _activityDeleteService = activityDeleteService ?? throw new ArgumentNullException(nameof(activityDeleteService));
        _trainingWriteService = trainingWriteService ?? throw new ArgumentNullException(nameof(trainingWriteService));
        _repositoryDeleteService = repositoryDeleteService ?? throw new ArgumentNullException(nameof(repositoryDeleteService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ApprovalDecisionResult> DecideAsync(
        ApprovalDecisionRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ApprovalDecisionResult.Forbidden("A signed-in user context is required.");
        }

        var isAdmin = user.IsInRole("Admin");
        var isHoD = user.IsInRole("HoD");
        if (!ApprovalAuthorization.CanApproveProjectChanges(isAdmin, isHoD))
        {
            return ApprovalDecisionResult.Forbidden("Only Admin or HoD users can decide approval requests.");
        }

        if (request.Decision == ApprovalDecisionAction.Reject && string.IsNullOrWhiteSpace(request.Remarks))
        {
            return ApprovalDecisionResult.ValidationFailed("A rejection reason is required.");
        }

        try
        {
            return request.ApprovalType switch
            {
                ApprovalQueueType.StageChange => await DecideStageChangeAsync(request, userId, isAdmin, isHoD, cancellationToken),
                ApprovalQueueType.ProjectMeta => await DecideMetaChangeAsync(request, userId, isAdmin, isHoD, cancellationToken),
                ApprovalQueueType.PlanApproval => await DecidePlanApprovalAsync(request, userId, isAdmin, isHoD, cancellationToken),
                ApprovalQueueType.DocRequest => await DecideDocumentRequestAsync(request, userId, isAdmin, isHoD, cancellationToken),
                ApprovalQueueType.TotRequest => await DecideTotRequestAsync(request, userId, isAdmin, isHoD, cancellationToken),
                ApprovalQueueType.ProliferationYearly => await DecideProliferationYearlyAsync(request, user, cancellationToken),
                ApprovalQueueType.ProliferationGranular => await DecideProliferationGranularAsync(request, user, cancellationToken),
                ApprovalQueueType.ActivityDelete => await DecideActivityDeleteAsync(request, cancellationToken),
                ApprovalQueueType.TrainingDelete => await DecideTrainingDeleteAsync(request, userId, cancellationToken),
                ApprovalQueueType.RepositoryDocumentDelete => await DecideRepositoryDocumentDeleteAsync(request, userId, cancellationToken),
                _ => ApprovalDecisionResult.ValidationFailed("Unsupported approval type.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent approval decision detected. Type={ApprovalType}, RequestId={RequestId}", request.ApprovalType, request.RequestId);
            return ApprovalDecisionResult.AlreadyDecided("This request changed while you were reviewing it. Refresh the Decision Centre and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected approval decision failure. Type={ApprovalType}, RequestId={RequestId}", request.ApprovalType, request.RequestId);
            return ApprovalDecisionResult.Error("The request could not be processed. No decision was recorded. Refresh and try again.");
        }
    }

    private async Task<ApprovalDecisionResult> DecideStageChangeAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid stage change request identifier.");
        }

        var input = new StageDecisionInput(
            id,
            request.Decision == ApprovalDecisionAction.Approve ? StageDecisionAction.Approve : StageDecisionAction.Reject,
            request.Remarks);

        var result = await _stageDecisionService.DecideAsync(input, userId, isAdmin, isHoD, cancellationToken);
        return result.Outcome switch
        {
            StageDecisionOutcome.Success => ApprovalDecisionResult.Success(request.Decision == ApprovalDecisionAction.Approve
                ? "Stage request approved. Dependent requests have been re-evaluated."
                : "Stage request rejected. Dependent requests have been re-evaluated."),
            StageDecisionOutcome.NotHeadOfDepartment => ApprovalDecisionResult.Forbidden("Only Admin or HoD users can decide stage changes."),
            StageDecisionOutcome.RequestNotFound => ApprovalDecisionResult.NotFound("Stage request not found."),
            StageDecisionOutcome.StageNotFound => ApprovalDecisionResult.NotFound("The requested project stage no longer exists."),
            StageDecisionOutcome.AlreadyDecided => ApprovalDecisionResult.AlreadyDecided("This request is no longer pending."),
            StageDecisionOutcome.ValidationFailed => ApprovalDecisionResult.ValidationFailed(result.Error ?? "The stage request is not ready for approval."),
            _ => ApprovalDecisionResult.Error("The stage request could not be processed.")
        };
    }

    private async Task<ApprovalDecisionResult> DecideMetaChangeAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid project information request identifier.");
        }

        var result = await _metaDecisionService.DecideAsync(
            new ProjectMetaDecisionInput(
                id,
                request.Decision == ApprovalDecisionAction.Approve ? ProjectMetaDecisionAction.Approve : ProjectMetaDecisionAction.Reject,
                request.Remarks),
            new ProjectMetaDecisionUser(userId, isAdmin, isHoD),
            cancellationToken);

        return result.Outcome switch
        {
            ProjectMetaDecisionOutcome.Success => ApprovalDecisionResult.Success(),
            ProjectMetaDecisionOutcome.Forbidden => ApprovalDecisionResult.Forbidden("You are not authorised to decide this request."),
            ProjectMetaDecisionOutcome.RequestNotFound => ApprovalDecisionResult.NotFound("Project information request not found."),
            ProjectMetaDecisionOutcome.AlreadyDecided => ApprovalDecisionResult.AlreadyDecided("This request is no longer pending."),
            ProjectMetaDecisionOutcome.ValidationFailed => ApprovalDecisionResult.ValidationFailed(result.Error ?? "The requested project changes are no longer valid."),
            _ => ApprovalDecisionResult.Error("The project information request could not be processed.")
        };
    }

    private async Task<ApprovalDecisionResult> DecidePlanApprovalAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid timeline approval identifier.");
        }

        var selectedPlan = await _db.PlanVersions
            .AsNoTracking()
            .Where(plan => plan.Id == id)
            .Select(plan => new { plan.Id, plan.ProjectId, plan.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (selectedPlan is null)
        {
            return ApprovalDecisionResult.NotFound("Timeline request not found.");
        }

        if (selectedPlan.Status != PlanVersionStatus.PendingApproval)
        {
            return ApprovalDecisionResult.AlreadyDecided("This timeline request is no longer pending.");
        }

        // The legacy domain service acts on the latest pending version. Verify that the
        // exact version reviewed in the Decision Centre is still that version before use.
        var currentPendingId = await _db.PlanVersions
            .AsNoTracking()
            .Where(plan => plan.ProjectId == selectedPlan.ProjectId && plan.Status == PlanVersionStatus.PendingApproval)
            .OrderByDescending(plan => plan.SubmittedOn)
            .ThenByDescending(plan => plan.VersionNo)
            .Select(plan => (int?)plan.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentPendingId != selectedPlan.Id)
        {
            return ApprovalDecisionResult.ValidationFailed("A newer timeline submission exists. Open the latest request before deciding.");
        }

        try
        {
            if (request.Decision == ApprovalDecisionAction.Approve)
            {
                var approved = await _planApprovalService.ApproveVersionAsync(selectedPlan.Id, userId, isAdmin, isHoD, cancellationToken);
                return approved
                    ? ApprovalDecisionResult.Success("Timeline approved and activated.")
                    : ApprovalDecisionResult.AlreadyDecided("The timeline request is no longer pending.");
            }

            var rejected = await _planApprovalService.RejectVersionAsync(
                selectedPlan.Id,
                userId,
                isAdmin,
                isHoD,
                request.Remarks,
                cancellationToken);

            return rejected
                ? ApprovalDecisionResult.Success("Timeline returned to the Project Officer.")
                : ApprovalDecisionResult.AlreadyDecided("The timeline request is no longer pending.");
        }
        catch (PlanApprovalValidationException ex)
        {
            return ApprovalDecisionResult.ValidationFailed(ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message);
        }
        catch (ForbiddenException ex)
        {
            return ApprovalDecisionResult.Forbidden(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApprovalDecisionResult.ValidationFailed(ex.Message);
        }
    }

    private async Task<ApprovalDecisionResult> DecideDocumentRequestAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid project document request identifier.");
        }

        try
        {
            if (request.Decision == ApprovalDecisionAction.Approve)
            {
                await _documentDecisionService.ApproveAsync(id, userId, isAdmin, isHoD, request.Remarks, cancellationToken);
            }
            else
            {
                await _documentDecisionService.RejectAsync(id, userId, isAdmin, isHoD, request.Remarks, cancellationToken);
            }

            return ApprovalDecisionResult.Success();
        }
        catch (ForbiddenException ex)
        {
            return ApprovalDecisionResult.Forbidden(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalDecisionResult.NotFound("Project document request not found.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("submitted", StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalDecisionResult.AlreadyDecided("This document request is no longer pending.");
        }
        catch (InvalidOperationException ex)
        {
            return ApprovalDecisionResult.ValidationFailed(ex.Message);
        }
    }

    private async Task<ApprovalDecisionResult> DecideTotRequestAsync(
        ApprovalDecisionRequest request,
        string userId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid Transfer of Technology request identifier.");
        }

        if (!TryDecodeRowVersion(request.RowVersion, out var rowVersion))
        {
            return ApprovalDecisionResult.ValidationFailed("The request changed or the concurrency token is invalid. Refresh and try again.");
        }

        var projectId = await _db.ProjectTotRequests
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);

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
            cancellationToken);

        return result.Status switch
        {
            ProjectTotRequestActionStatus.Success => ApprovalDecisionResult.Success(),
            ProjectTotRequestActionStatus.NotFound => ApprovalDecisionResult.NotFound("Transfer of Technology request not found."),
            ProjectTotRequestActionStatus.Forbidden => ApprovalDecisionResult.Forbidden(result.ErrorMessage ?? "You are not authorised to decide this request."),
            ProjectTotRequestActionStatus.Conflict => ApprovalDecisionResult.AlreadyDecided(result.ErrorMessage ?? "This request changed while you were reviewing it."),
            ProjectTotRequestActionStatus.ValidationFailed => ApprovalDecisionResult.ValidationFailed(result.ErrorMessage ?? "The Transfer of Technology request is not valid."),
            _ => ApprovalDecisionResult.Error("The Transfer of Technology request could not be processed.")
        };
    }

    private async Task<ApprovalDecisionResult> DecideProliferationYearlyAsync(
        ApprovalDecisionRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid yearly proliferation request identifier.");
        }

        var status = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => (int?)item.ApprovalStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (!status.HasValue)
        {
            return ApprovalDecisionResult.NotFound("Yearly proliferation request not found.");
        }

        if (status.Value != (int)ApprovalStatus.Pending)
        {
            return ApprovalDecisionResult.AlreadyDecided("This proliferation request is no longer pending.");
        }

        var result = await _proliferationService.DecideYearlyAsync(
            id,
            request.Decision == ApprovalDecisionAction.Approve,
            request.RowVersion,
            user,
            cancellationToken);

        return result.Success
            ? ApprovalDecisionResult.Success()
            : ApprovalDecisionResult.ValidationFailed(result.Error ?? "The proliferation request could not be processed.");
    }

    private async Task<ApprovalDecisionResult> DecideProliferationGranularAsync(
        ApprovalDecisionRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid unit-wise proliferation request identifier.");
        }

        var status = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => (int?)item.ApprovalStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (!status.HasValue)
        {
            return ApprovalDecisionResult.NotFound("Unit-wise proliferation request not found.");
        }

        if (status.Value != (int)ApprovalStatus.Pending)
        {
            return ApprovalDecisionResult.AlreadyDecided("This proliferation request is no longer pending.");
        }

        var result = await _proliferationService.DecideGranularAsync(
            id,
            request.Decision == ApprovalDecisionAction.Approve,
            request.RowVersion,
            user,
            cancellationToken);

        return result.Success
            ? ApprovalDecisionResult.Success()
            : ApprovalDecisionResult.ValidationFailed(result.Error ?? "The proliferation request could not be processed.");
    }

    private async Task<ApprovalDecisionResult> DecideActivityDeleteAsync(
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid activity deletion request identifier.");
        }

        try
        {
            if (request.Decision == ApprovalDecisionAction.Approve)
            {
                await _activityDeleteService.ApproveAsync(id, cancellationToken);
                return ApprovalDecisionResult.Success("Activity deleted.");
            }

            await _activityDeleteService.RejectAsync(id, request.Remarks, cancellationToken);
            return ApprovalDecisionResult.Success("Activity deletion request rejected.");
        }
        catch (ActivityAuthorizationException ex)
        {
            return ApprovalDecisionResult.Forbidden(ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return ApprovalDecisionResult.NotFound("Activity deletion request not found.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no longer pending", StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalDecisionResult.AlreadyDecided("This activity deletion request is no longer pending.");
        }
        catch (InvalidOperationException ex)
        {
            return ApprovalDecisionResult.ValidationFailed(ex.Message);
        }
    }

    private async Task<ApprovalDecisionResult> DecideTrainingDeleteAsync(
        ApprovalDecisionRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.RequestId, out var id))
        {
            return ApprovalDecisionResult.ValidationFailed("Invalid training deletion request identifier.");
        }

        var result = request.Decision == ApprovalDecisionAction.Approve
            ? await _trainingWriteService.ApproveDeleteAsync(id, userId, cancellationToken)
            : await _trainingWriteService.RejectDeleteAsync(id, request.Remarks ?? string.Empty, userId, cancellationToken);

        if (result.IsSuccess)
        {
            return ApprovalDecisionResult.Success(request.Decision == ApprovalDecisionAction.Approve
                ? "Training record deleted."
                : "Training deletion request rejected.");
        }

        return result.FailureCode switch
        {
            TrainingDeleteFailureCode.RequestNotFound => ApprovalDecisionResult.NotFound(result.ErrorMessage ?? "Training deletion request not found."),
            TrainingDeleteFailureCode.RequestNotPending => ApprovalDecisionResult.AlreadyDecided(result.ErrorMessage ?? "This training deletion request is no longer pending."),
            TrainingDeleteFailureCode.MissingUserId => ApprovalDecisionResult.Forbidden(result.ErrorMessage ?? "A signed-in approver is required."),
            TrainingDeleteFailureCode.ConcurrencyConflict => ApprovalDecisionResult.AlreadyDecided(result.ErrorMessage ?? "This request changed while you were reviewing it."),
            _ => ApprovalDecisionResult.ValidationFailed(result.ErrorMessage ?? "The training deletion request could not be processed.")
        };
    }

    private Task<ApprovalDecisionResult> DecideRepositoryDocumentDeleteAsync(
        ApprovalDecisionRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(request.RequestId, out var id))
        {
            return Task.FromResult(ApprovalDecisionResult.ValidationFailed("Invalid repository document request identifier."));
        }

        return _repositoryDeleteService.DecideAsync(
            id,
            request.Decision,
            userId,
            request.Remarks,
            cancellationToken);
    }

    private static bool TryDecodeRowVersion(string? encoded, out byte[]? rowVersion)
    {
        rowVersion = null;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return true;
        }

        try
        {
            rowVersion = Convert.FromBase64String(encoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
