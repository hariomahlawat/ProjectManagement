using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Stages;

public sealed class StageDecisionService
{
    private const string PendingDecisionStatus = "Pending";
    private const string ApprovedDecisionStatus = "Approved";
    private const string RejectedDecisionStatus = "Rejected";

    private const string ApprovedLogAction = "Approved";
    private const string RejectedLogAction = "Rejected";
    private const string AppliedLogAction = "Applied";

    private const string CompletionClampedWarning =
        "Completion date was earlier than the actual start date and has been adjusted to match the start date.";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly StageProgressService _stageProgressService;

    public StageDecisionService(ApplicationDbContext db, IClock clock, StageProgressService stageProgressService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _stageProgressService = stageProgressService ?? throw new ArgumentNullException(nameof(stageProgressService));
    }

    public async Task<StageDecisionResult> DecideAsync(StageDecisionInput input, string hodUserId, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(hodUserId));
        }

        var request = await _db.StageChangeRequests
            .SingleOrDefaultAsync(r => r.Id == input.RequestId, cancellationToken);

        if (request is null)
        {
            return StageDecisionResult.RequestNotFound();
        }

        var stage = await _db.ProjectStages
            .Include(s => s.Project)
            .SingleOrDefaultAsync(
                s => s.ProjectId == request.ProjectId && s.StageCode == request.StageCode,
                cancellationToken);

        if (stage is null)
        {
            return StageDecisionResult.StageNotFound();
        }

        if (!string.Equals(stage.Project?.HodUserId, hodUserId, StringComparison.Ordinal))
        {
            return StageDecisionResult.NotHeadOfDepartment();
        }

        if (!string.Equals(request.DecisionStatus, PendingDecisionStatus, StringComparison.Ordinal))
        {
            return StageDecisionResult.AlreadyDecided();
        }

        var trimmedNote = string.IsNullOrWhiteSpace(input.DecisionNote) ? null : input.DecisionNote.Trim();
        var now = _clock.UtcNow;

        var beforeStatus = stage.Status;
        var beforeActualStart = stage.ActualStart;
        var beforeCompletedOn = stage.CompletedOn;

        if (input.Action == StageDecisionAction.Reject)
        {
            request.DecisionStatus = RejectedDecisionStatus;
            request.DecidedByUserId = hodUserId;
            request.DecidedOn = now;
            request.DecisionNote = trimmedNote;

            var rejectionLog = new StageChangeLog
            {
                ProjectId = stage.ProjectId,
                StageCode = stage.StageCode,
                Action = RejectedLogAction,
                FromStatus = stage.Status.ToString(),
                ToStatus = stage.Status.ToString(),
                FromActualStart = stage.ActualStart,
                ToActualStart = stage.ActualStart,
                FromCompletedOn = stage.CompletedOn,
                ToCompletedOn = stage.CompletedOn,
                UserId = hodUserId,
                At = now,
                Note = trimmedNote
            };

            await _db.StageChangeLogs.AddAsync(rejectionLog, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return StageDecisionResult.Success(beforeStatus, beforeActualStart, beforeCompletedOn);
        }

        if (!Enum.TryParse<StageStatus>(request.RequestedStatus, ignoreCase: true, out var requestedStatus))
        {
            return StageDecisionResult.ValidationFailed("The requested status is not recognised.");
        }

        if (stage.Status == requestedStatus)
        {
            return StageDecisionResult.ValidationFailed("The project stage already has the requested status.");
        }

        if (!StageTransitionPolicy.TryValidateTransition(stage.Status, requestedStatus, request.RequestedDate, out var transitionError))
        {
            var message = string.IsNullOrEmpty(transitionError)
                ? $"Changing from {stage.Status} to {requestedStatus} is not allowed."
                : transitionError;
            return StageDecisionResult.ValidationFailed(message);
        }

        var warnings = new List<string>();
        DateOnly? effectiveDate = request.RequestedDate;

        if (requestedStatus == StageStatus.Completed)
        {
            if (!request.RequestedDate.HasValue)
            {
                return StageDecisionResult.ValidationFailed(
                    "A completion date is required when approving completion.");
            }

            if (stage.ActualStart.HasValue && request.RequestedDate.Value < stage.ActualStart.Value)
            {
                effectiveDate = stage.ActualStart;
                warnings.Add(CompletionClampedWarning);
            }
        }

        if (requestedStatus is StageStatus.InProgress or StageStatus.Completed)
        {
            var incompletePredecessors = await FindIncompletePredecessorsAsync(
                stage.ProjectId,
                stage.StageCode,
                cancellationToken);

            if (incompletePredecessors.Count > 0)
            {
                warnings.Add(
                    $"Predecessor stages are incomplete: {string.Join(", ", incompletePredecessors)}.");
            }
        }

        var useTransaction = _db.Database.IsRelational();
        await using var transaction = useTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await _stageProgressService.UpdateStageStatusAsync(
            stage.ProjectId,
            stage.StageCode,
            requestedStatus,
            effectiveDate,
            hodUserId,
            cancellationToken);

        await _db.Entry(stage).ReloadAsync(cancellationToken);

        var afterStatus = stage.Status;
        var afterActualStart = stage.ActualStart;
        var afterCompletedOn = stage.CompletedOn;
        var finalNote = CombineNoteAndWarnings(trimmedNote, warnings);

        request.DecisionStatus = ApprovedDecisionStatus;
        request.DecidedByUserId = hodUserId;
        request.DecidedOn = now;
        request.DecisionNote = finalNote;

        var approvedLog = new StageChangeLog
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            Action = ApprovedLogAction,
            FromStatus = beforeStatus.ToString(),
            ToStatus = afterStatus.ToString(),
            FromActualStart = beforeActualStart,
            ToActualStart = afterActualStart,
            FromCompletedOn = beforeCompletedOn,
            ToCompletedOn = afterCompletedOn,
            UserId = hodUserId,
            At = now,
            Note = finalNote
        };

        var appliedLog = new StageChangeLog
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            Action = AppliedLogAction,
            FromStatus = beforeStatus.ToString(),
            ToStatus = afterStatus.ToString(),
            FromActualStart = beforeActualStart,
            ToActualStart = afterActualStart,
            FromCompletedOn = beforeCompletedOn,
            ToCompletedOn = afterCompletedOn,
            UserId = hodUserId,
            At = now,
            Note = finalNote
        };

        await _db.StageChangeLogs.AddAsync(approvedLog, cancellationToken);
        await _db.StageChangeLogs.AddAsync(appliedLog, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return StageDecisionResult.Success(afterStatus, afterActualStart, afterCompletedOn, warnings);
    }

    private async Task<IReadOnlyList<string>> FindIncompletePredecessorsAsync(
        int projectId,
        string stageCode,
        CancellationToken cancellationToken)
    {
        var required = StageDependencies.RequiredPredecessors(stageCode);

        if (required.Count == 0)
        {
            return Array.Empty<string>();
        }

        var predecessors = await _db.ProjectStages
            .Where(ps => ps.ProjectId == projectId && required.Contains(ps.StageCode))
            .Select(ps => new { ps.StageCode, ps.Status })
            .ToListAsync(cancellationToken);

        var incomplete = new List<string>();

        foreach (var code in required)
        {
            var match = predecessors.FirstOrDefault(
                p => string.Equals(p.StageCode, code, StringComparison.OrdinalIgnoreCase));

            if (match is null || match.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                incomplete.Add(code);
            }
        }

        return incomplete;
    }

    private static string? CombineNoteAndWarnings(string? note, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return note;
        }

        var warningText = string.Join(Environment.NewLine, warnings.Select(w => $"Warning: {w}"));

        if (string.IsNullOrEmpty(note))
        {
            return warningText;
        }

        return note + Environment.NewLine + warningText;
    }
}

public sealed record StageDecisionInput(int RequestId, StageDecisionAction Action, string? DecisionNote);

public enum StageDecisionAction
{
    Approve,
    Reject
}

public sealed record StageDecisionStageSnapshot(StageStatus Status, DateOnly? ActualStart, DateOnly? CompletedOn);

public sealed record StageDecisionResult
{
    private StageDecisionResult(
        StageDecisionOutcome outcome,
        string? error,
        IReadOnlyList<string>? warnings,
        StageDecisionStageSnapshot? stage)
    {
        Outcome = outcome;
        Error = error;
        Warnings = warnings ?? Array.Empty<string>();
        Stage = stage;
    }

    public StageDecisionOutcome Outcome { get; }
    public string? Error { get; }
    public IReadOnlyList<string> Warnings { get; }

    public StageDecisionStageSnapshot? Stage { get; }

    public static StageDecisionResult Success(
        StageStatus status,
        DateOnly? actualStart,
        DateOnly? completedOn,
        IReadOnlyList<string>? warnings = null)
        => new(
            StageDecisionOutcome.Success,
            null,
            warnings,
            new StageDecisionStageSnapshot(status, actualStart, completedOn));

    public static StageDecisionResult NotHeadOfDepartment()
        => new(StageDecisionOutcome.NotHeadOfDepartment, null, Array.Empty<string>(), null);

    public static StageDecisionResult RequestNotFound()
        => new(StageDecisionOutcome.RequestNotFound, null, Array.Empty<string>(), null);

    public static StageDecisionResult StageNotFound()
        => new(StageDecisionOutcome.StageNotFound, null, Array.Empty<string>(), null);

    public static StageDecisionResult AlreadyDecided()
        => new(StageDecisionOutcome.AlreadyDecided, null, Array.Empty<string>(), null);

    public static StageDecisionResult ValidationFailed(string message)
        => new(StageDecisionOutcome.ValidationFailed, message, Array.Empty<string>(), null);
}

public enum StageDecisionOutcome
{
    Success,
    NotHeadOfDepartment,
    RequestNotFound,
    StageNotFound,
    AlreadyDecided,
    ValidationFailed
}
