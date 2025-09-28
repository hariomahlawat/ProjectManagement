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

public sealed class StageDirectApplyService
{
    private const string PendingDecisionStatus = "Pending";
    private const string SupersededDecisionStatus = "Superseded";
    private const string SupersededNote = "Superseded by HoD direct apply";
    private const string SupersededLogAction = "Superseded";
    private const string DirectApplyLogAction = "DirectApply";
    private const string AppliedLogAction = "Applied";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public StageDirectApplyService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DirectApplyResult> ApplyAsync(
        int projectId,
        string stageCode,
        string newStatus,
        DateOnly? date,
        string? note,
        string hodUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return DirectApplyResult.ValidationFailed("A stage code is required.");
        }

        if (string.IsNullOrWhiteSpace(newStatus))
        {
            return DirectApplyResult.ValidationFailed("A status is required.");
        }

        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(hodUserId));
        }

        var normalizedStageCode = stageCode.Trim().ToUpperInvariant();

        var stage = await _db.ProjectStages
            .Include(s => s.Project)
            .SingleOrDefaultAsync(
                s => s.ProjectId == projectId && s.StageCode == normalizedStageCode,
                ct);

        if (stage is null)
        {
            return DirectApplyResult.StageNotFound();
        }

        if (!string.Equals(stage.Project?.HodUserId, hodUserId, StringComparison.Ordinal))
        {
            return DirectApplyResult.NotHeadOfDepartment();
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var normalizedStatus = newStatus.Trim();
        var warnings = new List<string>();

        var isReopen = string.Equals(normalizedStatus, "Reopen", StringComparison.OrdinalIgnoreCase);
        StageStatus targetStatus;

        if (isReopen)
        {
            if (stage.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                return DirectApplyResult.ValidationFailed("Only completed or skipped stages can be reopened.");
            }

            targetStatus = date.HasValue ? StageStatus.InProgress : StageStatus.NotStarted;
        }
        else if (!Enum.TryParse<StageStatus>(normalizedStatus, ignoreCase: true, out targetStatus))
        {
            return DirectApplyResult.ValidationFailed("The new status is not recognised.");
        }
        else
        {
            if (targetStatus == stage.Status)
            {
                return DirectApplyResult.ValidationFailed("The stage is already in the requested status.");
            }

            if (!StageTransitionRules.IsTransitionAllowed(stage.Status, targetStatus))
            {
                return DirectApplyResult.ValidationFailed(
                    $"Changing from {stage.Status} to {targetStatus} is not allowed.");
            }
        }

        if (targetStatus is StageStatus.InProgress or StageStatus.Completed or StageStatus.Skipped)
        {
            var projectStages = await _db.ProjectStages
                .AsNoTracking()
                .Where(ps => ps.ProjectId == projectId)
                .ToListAsync(ct);

            var stageForWarnings = projectStages.Find(
                ps => string.Equals(ps.StageCode, normalizedStageCode, StringComparison.OrdinalIgnoreCase));

            if (stageForWarnings is not null)
            {
                var rulesService = new StageRulesService(_db);
                StageGuardResult guard = StageGuardResult.Allow();

                if (targetStatus == StageStatus.InProgress)
                {
                    var previousStatus = stageForWarnings.Status;
                    stageForWarnings.Status = StageStatus.NotStarted;
                    var context = await rulesService.BuildContextAsync(projectStages, ct);
                    stageForWarnings.Status = previousStatus;
                    guard = rulesService.CanStart(context, stageForWarnings.StageCode);
                }
                else if (targetStatus == StageStatus.Completed)
                {
                    var previousStatus = stageForWarnings.Status;
                    stageForWarnings.Status = StageStatus.InProgress;
                    var context = await rulesService.BuildContextAsync(projectStages, ct);
                    stageForWarnings.Status = previousStatus;
                    guard = rulesService.CanComplete(context, stageForWarnings.StageCode);
                }
                else if (targetStatus == StageStatus.Skipped)
                {
                    var context = await rulesService.BuildContextAsync(projectStages, ct);
                    guard = rulesService.CanSkip(context, stageForWarnings.StageCode);
                }

                if (!guard.Allowed && guard.Reason is not null)
                {
                    warnings.Add(guard.Reason);
                }
            }
        }

        var pendingRequest = await _db.StageChangeRequests
            .SingleOrDefaultAsync(
                r => r.ProjectId == stage.ProjectId
                    && r.StageCode == stage.StageCode
                    && r.DecisionStatus == PendingDecisionStatus,
                ct);

        var superseded = false;

        var originalStatus = stage.Status;
        var originalActualStart = stage.ActualStart;
        var originalCompletedOn = stage.CompletedOn;

        if (pendingRequest is not null)
        {
            superseded = true;
            pendingRequest.DecisionStatus = SupersededDecisionStatus;
            pendingRequest.DecidedByUserId = hodUserId;
            pendingRequest.DecidedOn = now;
            pendingRequest.DecisionNote = SupersededNote;

            var supersededLog = new StageChangeLog
            {
                ProjectId = stage.ProjectId,
                StageCode = stage.StageCode,
                Action = SupersededLogAction,
                FromStatus = originalStatus.ToString(),
                ToStatus = pendingRequest.RequestedStatus,
                FromActualStart = originalActualStart,
                ToActualStart = originalActualStart,
                FromCompletedOn = originalCompletedOn,
                ToCompletedOn = originalCompletedOn,
                UserId = hodUserId,
                At = now,
                Note = SupersededNote
            };

            await _db.StageChangeLogs.AddAsync(supersededLog, ct);
        }

        if (isReopen)
        {
            stage.CompletedOn = null;
            if (targetStatus == StageStatus.InProgress)
            {
                var startDate = date ?? today;
                stage.Status = StageStatus.InProgress;
                stage.ActualStart = startDate;
            }
            else
            {
                stage.Status = StageStatus.NotStarted;
                stage.ActualStart = null;
            }
        }
        else
        {
            switch (targetStatus)
            {
                case StageStatus.InProgress:
                {
                    var startDate = date ?? today;
                    stage.Status = StageStatus.InProgress;
                    stage.ActualStart ??= startDate;
                    stage.CompletedOn = null;
                    break;
                }
                case StageStatus.Completed:
                {
                    var completionDate = date ?? today;
                    stage.Status = StageStatus.Completed;
                    if (!stage.ActualStart.HasValue)
                    {
                        stage.ActualStart = completionDate;
                    }

                    if (stage.ActualStart.HasValue && completionDate < stage.ActualStart.Value)
                    {
                        completionDate = stage.ActualStart.Value;
                    }

                    stage.CompletedOn = completionDate;
                    break;
                }
                case StageStatus.Blocked:
                {
                    stage.Status = StageStatus.Blocked;
                    break;
                }
                case StageStatus.Skipped:
                {
                    stage.Status = StageStatus.Skipped;
                    break;
                }
                default:
                {
                    stage.Status = targetStatus;
                    break;
                }
            }
        }

        var finalStatus = stage.Status;
        var finalActualStart = stage.ActualStart;
        var finalCompletedOn = stage.CompletedOn;

        var directApplyLog = new StageChangeLog
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            Action = DirectApplyLogAction,
            FromStatus = originalStatus.ToString(),
            ToStatus = finalStatus.ToString(),
            FromActualStart = originalActualStart,
            ToActualStart = finalActualStart,
            FromCompletedOn = originalCompletedOn,
            ToCompletedOn = finalCompletedOn,
            UserId = hodUserId,
            At = now,
            Note = trimmedNote
        };

        var appliedLog = new StageChangeLog
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            Action = AppliedLogAction,
            FromStatus = originalStatus.ToString(),
            ToStatus = finalStatus.ToString(),
            FromActualStart = originalActualStart,
            ToActualStart = finalActualStart,
            FromCompletedOn = originalCompletedOn,
            ToCompletedOn = finalCompletedOn,
            UserId = hodUserId,
            At = now,
            Note = trimmedNote
        };

        await _db.StageChangeLogs.AddAsync(directApplyLog, ct);
        await _db.StageChangeLogs.AddAsync(appliedLog, ct);
        await _db.SaveChangesAsync(ct);

        return DirectApplyResult.Success(
            finalStatus,
            finalActualStart,
            finalCompletedOn,
            superseded,
            warnings);
    }
}

public sealed record DirectApplyResult
{
    public DirectApplyOutcome Outcome { get; }
    public string? Error { get; }
    public IReadOnlyList<string> Warnings { get; }
    public StageStatus? UpdatedStatus { get; }
    public DateOnly? ActualStart { get; }
    public DateOnly? CompletedOn { get; }
    public bool SupersededRequest { get; }

    private DirectApplyResult(
        DirectApplyOutcome outcome,
        string? error,
        IReadOnlyList<string>? warnings,
        StageStatus? updatedStatus,
        DateOnly? actualStart,
        DateOnly? completedOn,
        bool supersededRequest)
    {
        Outcome = outcome;
        Error = error;
        Warnings = NormalizeWarnings(warnings);
        UpdatedStatus = updatedStatus;
        ActualStart = actualStart;
        CompletedOn = completedOn;
        SupersededRequest = supersededRequest;
    }

    public static DirectApplyResult Success(
        StageStatus updatedStatus,
        DateOnly? actualStart,
        DateOnly? completedOn,
        bool supersededRequest,
        IReadOnlyList<string>? warnings = null)
        => new(DirectApplyOutcome.Success, null, warnings, updatedStatus, actualStart, completedOn, supersededRequest);

    public static DirectApplyResult StageNotFound()
        => new(DirectApplyOutcome.StageNotFound, null, Array.Empty<string>(), null, null, null, false);

    public static DirectApplyResult NotHeadOfDepartment()
        => new(DirectApplyOutcome.NotHeadOfDepartment, null, Array.Empty<string>(), null, null, null, false);

    public static DirectApplyResult ValidationFailed(string message)
        => new(DirectApplyOutcome.ValidationFailed, message, Array.Empty<string>(), null, null, null, false);

    private static IReadOnlyList<string> NormalizeWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (warnings is List<string> list)
        {
            return list.ToArray();
        }

        return new List<string>(warnings);
    }
}

public enum DirectApplyOutcome
{
    Success,
    StageNotFound,
    NotHeadOfDepartment,
    ValidationFailed
}
