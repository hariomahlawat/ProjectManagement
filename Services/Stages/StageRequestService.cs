using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Stages;

public class StageRequestService
{
    private const string PendingDecisionStatus = "Pending";
    private const string RequestedLogAction = "Requested";

    private static readonly IReadOnlyDictionary<StageStatus, IReadOnlyCollection<StageStatus>> AllowedTransitions =
        new Dictionary<StageStatus, IReadOnlyCollection<StageStatus>>
        {
            [StageStatus.NotStarted] = new[] { StageStatus.InProgress, StageStatus.Blocked },
            [StageStatus.InProgress] = new[] { StageStatus.Completed, StageStatus.Blocked },
            [StageStatus.Blocked] = new[] { StageStatus.InProgress }
        };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public StageRequestService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<StageRequestResult> RequestChangeAsync(
        StageChangeRequestInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(input.StageCode))
        {
            return StageRequestResult.ValidationFailed("A stage code is required.");
        }

        var stageCode = input.StageCode.Trim().ToUpperInvariant();

        var stage = await _db.ProjectStages
            .Include(s => s.Project)
            .SingleOrDefaultAsync(
                s => s.ProjectId == input.ProjectId && s.StageCode == stageCode,
                cancellationToken);

        if (stage is null)
        {
            return StageRequestResult.StageNotFound();
        }

        if (!string.Equals(stage.Project?.LeadPoUserId, userId, StringComparison.Ordinal))
        {
            return StageRequestResult.NotProjectOfficer();
        }

        if (!Enum.TryParse<StageStatus>(input.RequestedStatus, ignoreCase: true, out var requestedStatus))
        {
            return StageRequestResult.ValidationFailed("The requested status is not recognised.");
        }

        if (!AllowedTransitions.TryGetValue(stage.Status, out var allowed) || !allowed.Contains(requestedStatus))
        {
            return StageRequestResult.ValidationFailed(
                $"Changing from {stage.Status} to {requestedStatus} is not allowed.");
        }

        var requestedDate = input.RequestedDate;

        if (requestedStatus == StageStatus.Completed)
        {
            if (requestedDate is null)
            {
                return StageRequestResult.ValidationFailed(
                    "A completion date is required when requesting completion.");
            }

            if (stage.ActualStart.HasValue && requestedDate.Value < stage.ActualStart.Value)
            {
                return StageRequestResult.ValidationFailed(
                    "Completion date cannot be before the actual start date.");
            }
        }

        var trimmedNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

        var hasPending = await _db.StageChangeRequests
            .AnyAsync(
                r => r.ProjectId == stage.ProjectId
                    && r.StageCode == stage.StageCode
                    && r.DecisionStatus == PendingDecisionStatus,
                cancellationToken);

        if (hasPending)
        {
            return StageRequestResult.DuplicatePending();
        }

        var now = _clock.UtcNow;

        var request = new StageChangeRequest
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            RequestedStatus = requestedStatus.ToString(),
            RequestedDate = requestedDate,
            Note = trimmedNote,
            RequestedByUserId = userId,
            RequestedOn = now,
            DecisionStatus = PendingDecisionStatus
        };

        var log = new StageChangeLog
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            Action = RequestedLogAction,
            FromStatus = stage.Status.ToString(),
            ToStatus = requestedStatus.ToString(),
            FromActualStart = stage.ActualStart,
            ToActualStart = requestedStatus == StageStatus.InProgress ? requestedDate : null,
            FromCompletedOn = stage.CompletedOn,
            ToCompletedOn = requestedStatus == StageStatus.Completed ? requestedDate : null,
            UserId = userId,
            At = now,
            Note = trimmedNote
        };

        await _db.StageChangeRequests.AddAsync(request, cancellationToken);
        await _db.StageChangeLogs.AddAsync(log, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return StageRequestResult.Success();
    }
}

public sealed record StageChangeRequestInput
{
    public int ProjectId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string RequestedStatus { get; set; } = string.Empty;
    public DateOnly? RequestedDate { get; set; }
    public string? Note { get; set; }
}

public sealed record StageRequestResult(StageRequestOutcome Outcome, string? Error = null)
{
    public static StageRequestResult Success() => new(StageRequestOutcome.Success, null);
    public static StageRequestResult NotProjectOfficer() => new(StageRequestOutcome.NotProjectOfficer, null);
    public static StageRequestResult StageNotFound() => new(StageRequestOutcome.StageNotFound, null);
    public static StageRequestResult DuplicatePending() => new(StageRequestOutcome.DuplicatePending, "A pending request already exists for this stage.");
    public static StageRequestResult ValidationFailed(string message) => new(StageRequestOutcome.ValidationFailed, message);
}

public enum StageRequestOutcome
{
    Success,
    NotProjectOfficer,
    StageNotFound,
    DuplicatePending,
    ValidationFailed
}
