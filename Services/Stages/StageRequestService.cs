using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

public class StageRequestService
{
    private const string PendingDecisionStatus = "Pending";
    private const string RequestedLogAction = "Requested";
    private const string SupersededDecisionStatus = "Superseded";
    private const string SupersededLogAction = "Superseded";
    private const string SupersededNote = "Superseded by newer request";

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
        var operation = await CreateInternalAsync(input, userId, saveImmediately: true, cancellationToken);
        return operation.Result;
    }

    public async Task<BatchStageRequestResult> CreateBatchAsync(
        BatchStageChangeRequestInput input,
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

        if (input.ProjectId <= 0)
        {
            return BatchStageRequestResult.Invalid(new[] { "A valid project is required." });
        }

        var stages = input.Stages?.ToArray() ?? Array.Empty<StageChangeRequestItemInput>();
        if (stages.Length == 0)
        {
            return BatchStageRequestResult.Invalid(new[] { "At least one stage must be included." });
        }

        var responses = new List<StageRequestItemResult>();
        var operations = new List<StageRequestOperationResult>();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var stageInput in stages)
        {
            var single = new StageChangeRequestInput
            {
                ProjectId = input.ProjectId,
                StageCode = stageInput.StageCode,
                RequestedStatus = stageInput.RequestedStatus,
                RequestedDate = stageInput.RequestedDate,
                Note = stageInput.Note
            };

            var operation = await CreateInternalAsync(single, userId, saveImmediately: false, cancellationToken);

            responses.Add(new StageRequestItemResult(stageInput.StageCode, operation.Result));
            operations.Add(operation);

            if (operation.Result.Outcome != StageRequestOutcome.Success)
            {
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                return BatchStageRequestResult.FromResponses(responses);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var finalized = new List<StageRequestItemResult>(responses.Count);
        for (var i = 0; i < responses.Count; i++)
        {
            var response = responses[i];
            var operation = operations[i];
            if (response.Result.Outcome == StageRequestOutcome.Success && operation.Request is not null)
            {
                finalized.Add(new StageRequestItemResult(response.StageCode, StageRequestResult.Success(operation.Request.Id)));
            }
            else
            {
                finalized.Add(response);
            }
        }

        return BatchStageRequestResult.FromResponses(finalized);
    }

    private async Task<StageRequestOperationResult> CreateInternalAsync(
        StageChangeRequestInput input,
        string userId,
        bool saveImmediately,
        CancellationToken cancellationToken)
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
            return StageRequestOperationResult.From(StageRequestResult.ValidationFailed(new[] { "A stage code is required." }));
        }

        var stageCode = input.StageCode.Trim().ToUpperInvariant();

        var stage = await _db.ProjectStages
            .Include(s => s.Project)
            .SingleOrDefaultAsync(
                s => s.ProjectId == input.ProjectId && s.StageCode == stageCode,
                cancellationToken);

        if (stage is null)
        {
            return StageRequestOperationResult.From(StageRequestResult.StageNotFound());
        }

        if (!string.Equals(stage.Project?.LeadPoUserId, userId, StringComparison.Ordinal))
        {
            return StageRequestOperationResult.From(StageRequestResult.NotProjectOfficer());
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
            var errors = new List<string>();

            if (validation.Errors.Count > 0)
            {
                errors.AddRange(validation.Errors);
            }

            if (validation.MissingPredecessors.Count > 0 && errors.Count == 0)
            {
                errors.Add("Complete required predecessor stages first.");
            }

            if (errors.Count == 0)
            {
                errors.Add("Validation failed.");
            }

            return StageRequestOperationResult.From(StageRequestResult.ValidationFailed(errors, validation.MissingPredecessors));
        }

        var requestedDate = input.RequestedDate;
        var requestedStatus = Enum.Parse<StageStatus>(input.RequestedStatus, ignoreCase: true);

        var trimmedNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

        // SECTION: Supersede existing pending requests
        var pendingRequests = await _db.StageChangeRequests
            .Where(
                r => r.ProjectId == stage.ProjectId
                    && r.StageCode == stage.StageCode
                    && r.DecisionStatus == PendingDecisionStatus)
            .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;

        if (pendingRequests.Count > 0)
        {
            foreach (var pending in pendingRequests)
            {
                pending.DecisionStatus = SupersededDecisionStatus;
                pending.DecidedByUserId = userId;
                pending.DecidedOn = now;
                pending.DecisionNote = SupersededNote;

                var supersededLog = new StageChangeLog
                {
                    ProjectId = stage.ProjectId,
                    StageCode = stage.StageCode,
                    Action = SupersededLogAction,
                    FromStatus = stage.Status.ToString(),
                    ToStatus = pending.RequestedStatus,
                    FromActualStart = stage.ActualStart,
                    ToActualStart = stage.ActualStart,
                    FromCompletedOn = stage.CompletedOn,
                    ToCompletedOn = stage.CompletedOn,
                    UserId = userId,
                    At = now,
                    Note = SupersededNote
                };

                await _db.StageChangeLogs.AddAsync(supersededLog, cancellationToken);
            }
        }

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

        if (saveImmediately)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return StageRequestOperationResult.Success(request);
    }
}

public sealed record StageRequestResult
{
    public StageRequestOutcome Outcome { get; init; }
    public string? Error { get; init; }
    public int? RequestId { get; init; }
    public IReadOnlyList<string> MissingPredecessors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    private StageRequestResult(
        StageRequestOutcome outcome,
        string? error,
        int? requestId,
        IReadOnlyList<string> missingPredecessors,
        IReadOnlyList<string> errors)
    {
        Outcome = outcome;
        Error = error;
        RequestId = requestId;
        MissingPredecessors = missingPredecessors;
        Errors = errors;
    }

    public static StageRequestResult Success(int requestId)
        => new(StageRequestOutcome.Success, null, requestId, Array.Empty<string>(), Array.Empty<string>());

    public static StageRequestResult NotProjectOfficer()
        => new(StageRequestOutcome.NotProjectOfficer, null, null, Array.Empty<string>(), Array.Empty<string>());

    public static StageRequestResult StageNotFound()
        => new(StageRequestOutcome.StageNotFound, null, null, Array.Empty<string>(), Array.Empty<string>());

    public static StageRequestResult DuplicatePending()
        => new(
            StageRequestOutcome.DuplicatePending,
            "A pending request already exists for this stage.",
            null,
            Array.Empty<string>(),
            Array.Empty<string>());

    public static StageRequestResult ValidationFailed(
        IReadOnlyList<string>? errors,
        IReadOnlyList<string>? missingPredecessors = null)
    {
        var normalizedErrors = Normalize(errors);
        var normalizedMissing = Normalize(missingPredecessors);
        var message = normalizedErrors.Count > 0
            ? normalizedErrors[0]
            : "Validation failed.";

        return new(StageRequestOutcome.ValidationFailed, message, null, normalizedMissing, normalizedErrors);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (values is string[] array)
        {
            return array;
        }

        if (values is List<string> list)
        {
            return list.ToArray();
        }

        var copy = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            copy[i] = values[i];
        }

        return copy;
    }
}

internal sealed record StageRequestOperationResult(StageRequestResult Result, StageChangeRequest? Request)
{
    public static StageRequestOperationResult Success(StageChangeRequest request)
        => new(StageRequestResult.Success(request.Id), request);

    public static StageRequestOperationResult From(StageRequestResult result)
        => new(result, null);
}

// SECTION: Batch request result contracts
public sealed record StageRequestItemResult(string StageCode, StageRequestResult Result);

public sealed record BatchStageRequestResult
{
    public BatchStageRequestOutcome Outcome { get; init; }

    public IReadOnlyList<StageRequestItemResult> Items { get; init; }
        = Array.Empty<StageRequestItemResult>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    private BatchStageRequestResult(
        BatchStageRequestOutcome outcome,
        IReadOnlyList<StageRequestItemResult> items,
        IReadOnlyList<string> errors)
    {
        Outcome = outcome;
        Items = items;
        Errors = errors;
    }

    public static BatchStageRequestResult FromResponses(IReadOnlyList<StageRequestItemResult> responses)
    {
        if (responses.Count == 0)
        {
            return Invalid(new[] { "No requests were processed." });
        }

        var blocking = responses.FirstOrDefault(r => r.Result.Outcome != StageRequestOutcome.Success);

        if (blocking is null)
        {
            return Success(responses);
        }

        var errorMessages = blocking.Result.Errors;
        if (errorMessages.Count == 0 && !string.IsNullOrWhiteSpace(blocking.Result.Error))
        {
            errorMessages = new[] { blocking.Result.Error };
        }

        var outcome = blocking.Result.Outcome switch
        {
            StageRequestOutcome.NotProjectOfficer => BatchStageRequestOutcome.NotProjectOfficer,
            StageRequestOutcome.StageNotFound => BatchStageRequestOutcome.StageNotFound,
            _ => BatchStageRequestOutcome.ValidationFailed
        };

        return new BatchStageRequestResult(outcome, responses, errorMessages);
    }

    public static BatchStageRequestResult Success(IReadOnlyList<StageRequestItemResult> responses)
        => new(BatchStageRequestOutcome.Success, responses, Array.Empty<string>());

    public static BatchStageRequestResult Invalid(IReadOnlyList<string> errors)
        => new(BatchStageRequestOutcome.ValidationFailed, Array.Empty<StageRequestItemResult>(), errors);
}

public enum BatchStageRequestOutcome
{
    Success,
    NotProjectOfficer,
    StageNotFound,
    ValidationFailed
}

public enum StageRequestOutcome
{
    Success,
    NotProjectOfficer,
    StageNotFound,
    DuplicatePending,
    ValidationFailed
}
