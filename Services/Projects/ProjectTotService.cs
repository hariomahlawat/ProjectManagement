using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectTotService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProjectTotService(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ProjectTotUpdateResult> UpdateAsync(
        int projectId,
        ProjectTotUpdateRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update Transfer of Technology details.", nameof(actorUserId));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return ProjectTotUpdateResult.NotFound();
        }

        var todayLocal = GetTodayInIst();
        var validation = ValidateRequest(request, todayLocal, out var trimmedRemarks);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        ApplyTotUpdate(project, request, actorUserId, trimmedRemarks);
        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotUpdateResult.Success();
    }

    public async Task<ProjectTotRequestActionResult> SubmitRequestAsync(
        int projectId,
        ProjectTotUpdateRequest request,
        string submittedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submittedByUserId))
        {
            throw new ArgumentException("A valid user is required to submit a Transfer of Technology update.", nameof(submittedByUserId));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .Include(p => p.TotRequest)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return ProjectTotRequestActionResult.NotFound();
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return ProjectTotRequestActionResult.ValidationFailed("Transfer of Technology can only be updated once the project is completed.");
        }

        var todayLocal = GetTodayInIst();
        var validation = ValidateRequest(request, todayLocal, out var trimmedRemarks);
        if (!validation.IsSuccess)
        {
            return ProjectTotRequestActionResult.ValidationFailed(validation.ErrorMessage ?? "Unable to validate Transfer of Technology details.");
        }

        var totRequest = project.TotRequest;
        if (totRequest is null)
        {
            totRequest = new ProjectTotRequest
            {
                ProjectId = project.Id,
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            project.TotRequest = totRequest;
            _db.ProjectTotRequests.Add(totRequest);
        }
        else if (totRequest.DecisionState == ProjectTotRequestDecisionState.Pending)
        {
            return ProjectTotRequestActionResult.Conflict("A Transfer of Technology update is already pending approval for this project.");
        }

        totRequest.ProposedStatus = request.Status;
        totRequest.ProposedStartedOn = request.StartedOn;
        totRequest.ProposedCompletedOn = request.CompletedOn;
        totRequest.ProposedRemarks = trimmedRemarks;
        totRequest.SubmittedByUserId = submittedByUserId;
        totRequest.SubmittedOnUtc = _clock.UtcNow.UtcDateTime;
        totRequest.DecisionState = ProjectTotRequestDecisionState.Pending;
        totRequest.DecidedByUserId = null;
        totRequest.DecidedOnUtc = null;
        totRequest.DecisionRemarks = null;
        totRequest.SubmittedByUser = null!;
        totRequest.DecidedByUser = null;
        totRequest.RowVersion = Guid.NewGuid().ToByteArray();

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotRequestActionResult.Success();
    }

    public async Task<ProjectTotRequestActionResult> DecideRequestAsync(
        int projectId,
        bool approve,
        string decisionUserId,
        string? decisionRemarks,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionUserId))
        {
            throw new ArgumentException("A valid user is required to decide on a Transfer of Technology request.", nameof(decisionUserId));
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .Include(p => p.TotRequest)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null || project.TotRequest is null)
        {
            return ProjectTotRequestActionResult.NotFound();
        }

        var request = project.TotRequest;

        if (request.DecisionState != ProjectTotRequestDecisionState.Pending)
        {
            return ProjectTotRequestActionResult.Conflict("The pending Transfer of Technology request has already been decided.");
        }

        if (expectedRowVersion is not null && !request.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return ProjectTotRequestActionResult.Conflict("The Transfer of Technology request was modified by another user. Refresh the page to continue.");
        }

        var trimmedDecisionRemarks = string.IsNullOrWhiteSpace(decisionRemarks)
            ? null
            : decisionRemarks.Trim();

        if (trimmedDecisionRemarks is { Length: > 2000 })
        {
            return ProjectTotRequestActionResult.ValidationFailed("Decision remarks must be 2000 characters or fewer.");
        }

        if (approve)
        {
            var updateRequest = new ProjectTotUpdateRequest(
                request.ProposedStatus,
                request.ProposedStartedOn,
                request.ProposedCompletedOn,
                request.ProposedRemarks);

            var todayLocal = GetTodayInIst();
            var validation = ValidateRequest(updateRequest, todayLocal, out var trimmedRemarks);
            if (!validation.IsSuccess)
            {
                return ProjectTotRequestActionResult.ValidationFailed(validation.ErrorMessage ?? "Unable to approve the Transfer of Technology request.");
            }

            ApplyTotUpdate(project, updateRequest, decisionUserId, trimmedRemarks);
            request.DecisionState = ProjectTotRequestDecisionState.Approved;
        }
        else
        {
            request.DecisionState = ProjectTotRequestDecisionState.Rejected;
        }

        request.DecidedByUserId = decisionUserId;
        request.DecidedOnUtc = _clock.UtcNow.UtcDateTime;
        request.DecisionRemarks = trimmedDecisionRemarks;
        request.DecidedByUser = null;
        request.RowVersion = Guid.NewGuid().ToByteArray();

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotRequestActionResult.Success();
    }

    private DateOnly GetTodayInIst() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        _clock.UtcNow.UtcDateTime,
        TimeZoneHelper.GetIst()));

    private ProjectTotUpdateResult ValidateRequest(ProjectTotUpdateRequest request, DateOnly todayLocal, out string? trimmedRemarks)
    {
        trimmedRemarks = string.IsNullOrWhiteSpace(request.Remarks)
            ? null
            : request.Remarks.Trim();

        if (trimmedRemarks is { Length: > 2000 })
        {
            return ProjectTotUpdateResult.ValidationFailed("Remarks must be 2000 characters or fewer.");
        }

        switch (request.Status)
        {
            case ProjectTotStatus.NotRequired:
            {
                if (request.StartedOn.HasValue || request.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Start and completion dates must be empty when ToT is not required.");
                }

                break;
            }
            case ProjectTotStatus.NotStarted:
            {
                if (request.StartedOn.HasValue || request.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Start and completion dates must be empty until ToT is in progress.");
                }

                break;
            }
            case ProjectTotStatus.InProgress:
            {
                if (request.StartedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date is required when ToT is in progress.");
                }

                if (request.StartedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date cannot be in the future.");
                }

                if (request.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Completion date must be empty until ToT is completed.");
                }

                break;
            }
            case ProjectTotStatus.Completed:
            {
                if (request.StartedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date is required when ToT is completed.");
                }

                if (request.CompletedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Completion date is required when ToT is completed.");
                }

                if (request.CompletedOn.Value < request.StartedOn.Value)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Completion date cannot be earlier than the start date.");
                }

                if (request.StartedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date cannot be in the future.");
                }

                if (request.CompletedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Completion date cannot be in the future.");
                }

                break;
            }
            default:
            {
                return ProjectTotUpdateResult.ValidationFailed("Invalid ToT status specified.");
            }
        }

        return ProjectTotUpdateResult.Success();
    }

    private void ApplyTotUpdate(Project project, ProjectTotUpdateRequest request, string actorUserId, string? trimmedRemarks)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        var tot = project.Tot;
        if (tot is null)
        {
            tot = new ProjectTot
            {
                ProjectId = project.Id
            };
            project.Tot = tot;
            _db.ProjectTots.Add(tot);
        }

        tot.Status = request.Status;
        tot.Remarks = trimmedRemarks;

        switch (request.Status)
        {
            case ProjectTotStatus.NotRequired:
            case ProjectTotStatus.NotStarted:
                tot.StartedOn = null;
                tot.CompletedOn = null;
                break;
            case ProjectTotStatus.InProgress:
                tot.StartedOn = request.StartedOn;
                tot.CompletedOn = null;
                break;
            case ProjectTotStatus.Completed:
                tot.StartedOn = request.StartedOn;
                tot.CompletedOn = request.CompletedOn;
                break;
        }

        tot.LastApprovedByUserId = actorUserId;
        tot.LastApprovedOnUtc = _clock.UtcNow.UtcDateTime;
    }
}
