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

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IStageValidationService _validationService;

    public StageRequestService(
        ApplicationDbContext db,
        IClock clock,
        IStageValidationService validationService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validationService = validationService
            ?? throw new ArgumentNullException(nameof(validationService));
    }

    public async Task<StageRequestResult> CreateAsync(
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

        var validation = await _validationService.ValidateAsync(
            stage.ProjectId,
            stage.StageCode,
            input.RequestedStatus,
            input.RequestedDate,
            isHoD: false,
            cancellationToken);

        if (!validation.IsValid)
        {
            var details = validation.Errors
                .Concat(validation.Warnings)
                .ToList();

            if (details.Count == 0 && validation.MissingPredecessors.Count > 0)
            {
                details.Add("Complete required predecessor stages first.");
            }

            return StageRequestResult.ValidationFailed(
                "validation",
                details.Count == 0 ? Array.Empty<string>() : details,
                validation.MissingPredecessors);
        }

        var requestedDate = input.RequestedDate;
        var requestedStatus = Enum.Parse<StageStatus>(input.RequestedStatus, ignoreCase: true);

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

        return StageRequestResult.Success(request.Id);
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

public sealed record StageRequestResult(
    StageRequestOutcome Outcome,
    string? Error = null,
    int? RequestId = null,
    IReadOnlyList<string>? Details = null,
    IReadOnlyList<string>? MissingPredecessors = null)
{
    public static StageRequestResult Success(int requestId) => new(StageRequestOutcome.Success, null, requestId);
    public static StageRequestResult NotProjectOfficer() => new(StageRequestOutcome.NotProjectOfficer, null);
    public static StageRequestResult StageNotFound() => new(StageRequestOutcome.StageNotFound, null);
    public static StageRequestResult DuplicatePending() => new(StageRequestOutcome.DuplicatePending, "A pending request already exists for this stage.");
    public static StageRequestResult ValidationFailed(
        string message,
        IReadOnlyList<string>? details = null,
        IReadOnlyList<string>? missingPredecessors = null)
        => new(StageRequestOutcome.ValidationFailed, message, null, details, missingPredecessors);
}

public enum StageRequestOutcome
{
    Success,
    NotProjectOfficer,
    StageNotFound,
    DuplicatePending,
    ValidationFailed
}
