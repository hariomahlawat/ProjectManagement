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
    private const string AutoBackfillLogAction = "AutoBackfill";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IStageValidationService _validationService;

    public StageDirectApplyService(
        ApplicationDbContext db,
        IClock clock,
        IStageValidationService validationService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validationService = validationService
            ?? throw new ArgumentNullException(nameof(validationService));
    }

    public async Task<DirectApplyResult> ApplyAsync(
        int projectId,
        string stageCode,
        string newStatus,
        DateOnly? date,
        string? note,
        string hodUserId,
        bool forceBackfillPredecessors,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return DirectApplyResult.ValidationFailed(
                "validation",
                new[] { "A stage code is required." });
        }

        if (string.IsNullOrWhiteSpace(newStatus))
        {
            return DirectApplyResult.ValidationFailed(
                "validation",
                new[] { "A status is required." });
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
        var isReopen = string.Equals(normalizedStatus, "Reopen", StringComparison.OrdinalIgnoreCase);
        var validationTargetStatus = isReopen
            ? (date.HasValue ? StageStatus.InProgress : StageStatus.NotStarted).ToString()
            : normalizedStatus;

        var validation = await _validationService.ValidateAsync(
            projectId,
            normalizedStageCode,
            validationTargetStatus,
            date,
            isHoD: true,
            ct);

        var validationDetails = validation.Errors
            .Concat(validation.Warnings)
            .ToList();

        if (validationDetails.Count == 0 && validation.MissingPredecessors.Count > 0)
        {
            validationDetails.Add("Complete required predecessor stages first.");
        }

        if (validation.Errors.Count > 0)
        {
            return DirectApplyResult.ValidationFailed(
                "validation",
                validationDetails.Count == 0 ? Array.Empty<string>() : validationDetails,
                validation.MissingPredecessors);
        }

        var warnings = new List<string>(validation.Warnings);

        StageStatus targetStatus;
        if (isReopen)
        {
            targetStatus = date.HasValue ? StageStatus.InProgress : StageStatus.NotStarted;
        }
        else if (!Enum.TryParse<StageStatus>(validationTargetStatus, ignoreCase: true, out targetStatus))
        {
            return DirectApplyResult.ValidationFailed(
                "validation",
                new[] { "The new status is not recognised." });
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

        if (validation.MissingPredecessors.Count > 0)
        {
            if (!forceBackfillPredecessors)
            {
                return DirectApplyResult.ValidationFailed(
                    "validation",
                    validationDetails.Count == 0 ? Array.Empty<string>() : validationDetails,
                    validation.MissingPredecessors);
            }

            var predecessorCodes = validation.MissingPredecessors.ToArray();
            var predecessors = await _db.ProjectStages
                .Where(ps => ps.ProjectId == projectId && predecessorCodes.Contains(ps.StageCode))
                .ToListAsync(ct);

            var predecessorLookup = predecessors
                .ToDictionary(ps => ps.StageCode, StringComparer.OrdinalIgnoreCase);

            foreach (var predecessorCode in predecessorCodes)
            {
                if (!predecessorLookup.TryGetValue(predecessorCode, out var predecessor))
                {
                    continue;
                }

                var predecessorOriginalStatus = predecessor.Status;
                var predecessorOriginalActualStart = predecessor.ActualStart;
                var predecessorOriginalCompletedOn = predecessor.CompletedOn;

                predecessor.Status = StageStatus.Completed;
                predecessor.ActualStart = null;
                predecessor.CompletedOn = null;

                var autoBackfillLog = new StageChangeLog
                {
                    ProjectId = predecessor.ProjectId,
                    StageCode = predecessor.StageCode,
                    Action = AutoBackfillLogAction,
                    FromStatus = predecessorOriginalStatus.ToString(),
                    ToStatus = StageStatus.Completed.ToString(),
                    FromActualStart = predecessorOriginalActualStart,
                    ToActualStart = null,
                    FromCompletedOn = predecessorOriginalCompletedOn,
                    ToCompletedOn = null,
                    UserId = hodUserId,
                    At = now,
                    Note = $"Auto-backfilled (no dates) due to completion of {stage.StageCode}."
                };

                await _db.StageChangeLogs.AddAsync(autoBackfillLog, ct);
            }
        }

        if (isReopen)
        {
            stage.CompletedOn = null;
            if (targetStatus == StageStatus.InProgress)
            {
                var startDate = date ?? today;
                stage.Status = StageStatus.InProgress;

                if (validation.SuggestedAutoStart.HasValue && startDate >= validation.SuggestedAutoStart.Value)
                {
                    stage.ActualStart = validation.SuggestedAutoStart.Value;
                }
                else
                {
                    stage.ActualStart = startDate;
                }
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

                    if (!stage.ActualStart.HasValue)
                    {
                        if (validation.SuggestedAutoStart.HasValue && startDate >= validation.SuggestedAutoStart.Value)
                        {
                            stage.ActualStart = validation.SuggestedAutoStart.Value;
                        }
                        else
                        {
                            stage.ActualStart = startDate;
                        }
                    }

                    stage.CompletedOn = null;
                    break;
                }
                case StageStatus.Completed:
                {
                    stage.Status = StageStatus.Completed;

                    if (!date.HasValue)
                    {
                        stage.ActualStart = null;
                        stage.CompletedOn = null;
                        warnings.Add("Incomplete data");
                        break;
                    }

                    var completionDate = date.Value;

                    if (!stage.ActualStart.HasValue)
                    {
                        if (validation.SuggestedAutoStart.HasValue && completionDate >= validation.SuggestedAutoStart.Value)
                        {
                            stage.ActualStart = validation.SuggestedAutoStart.Value;
                        }
                        else
                        {
                            stage.ActualStart = completionDate;
                        }
                    }

                    if (stage.ActualStart.HasValue && completionDate < stage.ActualStart.Value)
                    {
                        stage.CompletedOn = stage.ActualStart.Value;
                        warnings.Add("Completion date adjusted to match actual start.");
                    }
                    else
                    {
                        stage.CompletedOn = completionDate;
                    }

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
    public IReadOnlyList<string> Details { get; }
    public IReadOnlyList<string> MissingPredecessors { get; }
    public IReadOnlyList<string> Warnings { get; }
    public StageStatus? UpdatedStatus { get; }
    public DateOnly? ActualStart { get; }
    public DateOnly? CompletedOn { get; }
    public bool SupersededRequest { get; }

    private DirectApplyResult(
        DirectApplyOutcome outcome,
        string? error,
        IReadOnlyList<string>? details,
        IReadOnlyList<string>? missingPredecessors,
        IReadOnlyList<string>? warnings,
        StageStatus? updatedStatus,
        DateOnly? actualStart,
        DateOnly? completedOn,
        bool supersededRequest)
    {
        Outcome = outcome;
        Error = error;
        Details = Normalize(details);
        MissingPredecessors = Normalize(missingPredecessors);
        Warnings = Normalize(warnings);
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
        => new(DirectApplyOutcome.Success, null, Array.Empty<string>(), Array.Empty<string>(), warnings, updatedStatus, actualStart, completedOn, supersededRequest);

    public static DirectApplyResult StageNotFound()
        => new(DirectApplyOutcome.StageNotFound, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, null, null, false);

    public static DirectApplyResult NotHeadOfDepartment()
        => new(DirectApplyOutcome.NotHeadOfDepartment, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, null, null, false);

    public static DirectApplyResult ValidationFailed(
        string message,
        IReadOnlyList<string>? details = null,
        IReadOnlyList<string>? missingPredecessors = null)
        => new(DirectApplyOutcome.ValidationFailed, message, details, missingPredecessors, Array.Empty<string>(), null, null, null, false);

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (values is List<string> list)
        {
            return list.ToArray();
        }

        return new List<string>(values);
    }
}

public enum DirectApplyOutcome
{
    Success,
    StageNotFound,
    NotHeadOfDepartment,
    ValidationFailed
}
