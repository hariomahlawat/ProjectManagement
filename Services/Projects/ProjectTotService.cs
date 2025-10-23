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
        var validation = ValidateRequest(request, todayLocal, out var normalizedRequest);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        ApplyTotUpdate(project, normalizedRequest, actorUserId);
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
            return ProjectTotRequestActionResult.ValidationFailed("Unable to identify the submitting user. Please sign in again.");
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
        var validation = ValidateRequest(request, todayLocal, out var normalizedRequest);
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

        totRequest.ProposedStatus = normalizedRequest.Status;
        totRequest.ProposedStartedOn = normalizedRequest.StartedOn;
        totRequest.ProposedCompletedOn = normalizedRequest.CompletedOn;
        totRequest.ProposedMetDetails = normalizedRequest.MetDetails;
        totRequest.ProposedMetCompletedOn = normalizedRequest.MetCompletedOn;
        totRequest.ProposedFirstProductionModelManufactured = normalizedRequest.FirstProductionModelManufactured;
        totRequest.ProposedFirstProductionModelManufacturedOn = normalizedRequest.FirstProductionModelManufacturedOn;
        totRequest.SubmittedByUserId = submittedByUserId;
        totRequest.SubmittedOnUtc = _clock.UtcNow.UtcDateTime;
        totRequest.DecisionState = ProjectTotRequestDecisionState.Pending;
        totRequest.DecidedByUserId = null;
        totRequest.DecidedOnUtc = null;
        totRequest.DecidedByUser = null;
        totRequest.RowVersion = Guid.NewGuid().ToByteArray();

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotRequestActionResult.Success();
    }

    public async Task<ProjectTotRequestActionResult> DecideRequestAsync(
        int projectId,
        bool approve,
        string decisionUserId,
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

        if (approve)
        {
            var updateRequest = new ProjectTotUpdateRequest(
                request.ProposedStatus,
                request.ProposedStartedOn,
                request.ProposedCompletedOn,
                request.ProposedMetDetails,
                request.ProposedMetCompletedOn,
                request.ProposedFirstProductionModelManufactured,
                request.ProposedFirstProductionModelManufacturedOn);

            var todayLocal = GetTodayInIst();
            var validation = ValidateRequest(updateRequest, todayLocal, out var normalizedRequest);
            if (!validation.IsSuccess)
            {
                return ProjectTotRequestActionResult.ValidationFailed(validation.ErrorMessage ?? "Unable to approve the Transfer of Technology request.");
            }

            ApplyTotUpdate(project, normalizedRequest, decisionUserId);
            request.DecisionState = ProjectTotRequestDecisionState.Approved;
        }
        else
        {
            request.DecisionState = ProjectTotRequestDecisionState.Rejected;
        }

        request.DecidedByUserId = decisionUserId;
        request.DecidedOnUtc = _clock.UtcNow.UtcDateTime;
        request.DecidedByUser = null;
        request.RowVersion = Guid.NewGuid().ToByteArray();

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotRequestActionResult.Success();
    }

    private DateOnly GetTodayInIst() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        _clock.UtcNow.UtcDateTime,
        TimeZoneHelper.GetIst()));

    private ProjectTotUpdateResult ValidateRequest(
        ProjectTotUpdateRequest request,
        DateOnly todayLocal,
        out ProjectTotUpdateRequest normalizedRequest)
    {
        var trimmedMetDetails = string.IsNullOrWhiteSpace(request.MetDetails)
            ? null
            : request.MetDetails.Trim();

        if (trimmedMetDetails is { Length: > 2000 })
        {
            normalizedRequest = request with
            {
                MetDetails = trimmedMetDetails
            };
            return ProjectTotUpdateResult.ValidationFailed("MET details must be 2000 characters or fewer.");
        }

        if (request.MetCompletedOn.HasValue && request.MetCompletedOn.Value > todayLocal)
        {
            normalizedRequest = request with
            {
                MetDetails = trimmedMetDetails
            };
            return ProjectTotUpdateResult.ValidationFailed("MET completion date cannot be in the future.");
        }

        if (request.FirstProductionModelManufactured is true && request.FirstProductionModelManufacturedOn is null)
        {
            normalizedRequest = request with
            {
                MetDetails = trimmedMetDetails
            };
            return ProjectTotUpdateResult.ValidationFailed("First production model manufacture date is required when marked as manufactured.");
        }

        if (request.FirstProductionModelManufactured is not true && request.FirstProductionModelManufacturedOn.HasValue)
        {
            normalizedRequest = request with
            {
                MetDetails = trimmedMetDetails
            };
            return ProjectTotUpdateResult.ValidationFailed("First production model manufacture date must be empty unless marked as manufactured.");
        }

        if (request.FirstProductionModelManufacturedOn.HasValue && request.FirstProductionModelManufacturedOn.Value > todayLocal)
        {
            normalizedRequest = request with
            {
                MetDetails = trimmedMetDetails
            };
            return ProjectTotUpdateResult.ValidationFailed("First production model manufacture date cannot be in the future.");
        }

        normalizedRequest = request with
        {
            MetDetails = trimmedMetDetails
        };

        switch (normalizedRequest.Status)
        {
            case ProjectTotStatus.NotRequired:
            {
                if (normalizedRequest.StartedOn.HasValue || normalizedRequest.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Start and completion dates must be empty when ToT is not required.");
                }

                if (!string.IsNullOrEmpty(normalizedRequest.MetDetails) ||
                    normalizedRequest.MetCompletedOn.HasValue ||
                    normalizedRequest.FirstProductionModelManufactured.HasValue ||
                    normalizedRequest.FirstProductionModelManufacturedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "MET and first production model details must be empty when ToT is not required.");
                }

                normalizedRequest = normalizedRequest with
                {
                    StartedOn = null,
                    CompletedOn = null,
                    MetDetails = null,
                    MetCompletedOn = null,
                    FirstProductionModelManufactured = null,
                    FirstProductionModelManufacturedOn = null
                };

                break;
            }
            case ProjectTotStatus.NotStarted:
            {
                if (normalizedRequest.StartedOn.HasValue || normalizedRequest.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Start and completion dates must be empty until ToT is in progress.");
                }

                break;
            }
            case ProjectTotStatus.InProgress:
            {
                if (normalizedRequest.StartedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date is required when ToT is in progress.");
                }

                if (normalizedRequest.StartedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date cannot be in the future.");
                }

                if (normalizedRequest.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Completion date must be empty until ToT is completed.");
                }

                break;
            }
            case ProjectTotStatus.Completed:
            {
                if (normalizedRequest.StartedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date is required when ToT is completed.");
                }

                if (normalizedRequest.CompletedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Completion date is required when ToT is completed.");
                }

                if (normalizedRequest.CompletedOn.Value < normalizedRequest.StartedOn.Value)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Completion date cannot be earlier than the start date.");
                }

                if (normalizedRequest.StartedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date cannot be in the future.");
                }

                if (normalizedRequest.CompletedOn.Value > todayLocal)
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

        if (normalizedRequest.StartedOn.HasValue)
        {
            var start = normalizedRequest.StartedOn.Value;

            if (normalizedRequest.MetCompletedOn.HasValue && normalizedRequest.MetCompletedOn.Value < start)
            {
                return ProjectTotUpdateResult.ValidationFailed("MET completion date cannot be earlier than the ToT start date.");
            }

            if (normalizedRequest.FirstProductionModelManufacturedOn.HasValue
                && normalizedRequest.FirstProductionModelManufacturedOn.Value < start)
            {
                return ProjectTotUpdateResult.ValidationFailed("First production model date cannot be earlier than the ToT start date.");
            }
        }

        if (normalizedRequest.CompletedOn.HasValue)
        {
            var completion = normalizedRequest.CompletedOn.Value;

            if (normalizedRequest.MetCompletedOn.HasValue && normalizedRequest.MetCompletedOn.Value > completion)
            {
                return ProjectTotUpdateResult.ValidationFailed("MET completion date cannot be later than the ToT completion date.");
            }

            if (normalizedRequest.FirstProductionModelManufacturedOn.HasValue
                && normalizedRequest.FirstProductionModelManufacturedOn.Value > completion)
            {
                return ProjectTotUpdateResult.ValidationFailed("First production model date cannot be later than the ToT completion date.");
            }
        }

        return ProjectTotUpdateResult.Success();
    }

    private void ApplyTotUpdate(Project project, ProjectTotUpdateRequest request, string actorUserId)
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
        tot.MetDetails = request.MetDetails;
        tot.MetCompletedOn = request.MetCompletedOn;
        tot.FirstProductionModelManufactured = request.FirstProductionModelManufactured;
        tot.FirstProductionModelManufacturedOn = request.FirstProductionModelManufacturedOn;

        switch (request.Status)
        {
            case ProjectTotStatus.NotRequired:
            case ProjectTotStatus.NotStarted:
                tot.StartedOn = null;
                tot.CompletedOn = null;
                if (request.Status == ProjectTotStatus.NotRequired)
                {
                    tot.MetDetails = null;
                    tot.MetCompletedOn = null;
                    tot.FirstProductionModelManufactured = null;
                    tot.FirstProductionModelManufacturedOn = null;
                }
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
