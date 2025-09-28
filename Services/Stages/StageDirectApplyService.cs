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
    private const string AdminCompletionNote = "Administrative completion (no dates) by HoD";
    private const string AutoBackfillNoteTemplate = "Auto-backfilled (no dates) due to completion of {0}";
    private const string ClampWarning = "CompletedOn was clamped to ActualStart";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IStageValidationService _validator;

    public StageDirectApplyService(
        ApplicationDbContext db,
        IClock clock,
        IStageValidationService validator)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<DirectApplyResult> ApplyAsync(
        int projectId,
        string stageCode,
        string status,
        DateOnly? date,
        string? note,
        string hodUserId,
        bool forceBackfillPredecessors,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            throw StageDirectApplyValidationException.FromMessages("A stage code is required.");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            throw StageDirectApplyValidationException.FromMessages("A status is required.");
        }

        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(hodUserId));
        }

        var normalizedStageCode = stageCode.Trim().ToUpperInvariant();
        var normalizedStatus = status.Trim();
        var isReopen = string.Equals(normalizedStatus, "Reopen", StringComparison.OrdinalIgnoreCase);
        var validationTargetStatus = isReopen
            ? (date.HasValue ? StageStatus.InProgress : StageStatus.NotStarted).ToString()
            : normalizedStatus;

        var stage = await _db.ProjectStages
            .Include(s => s.Project)
            .SingleOrDefaultAsync(
                s => s.ProjectId == projectId && s.StageCode == normalizedStageCode,
                ct);

        if (stage is null)
        {
            throw new StageDirectApplyNotFoundException();
        }

        if (!string.Equals(stage.Project?.HodUserId, hodUserId, StringComparison.Ordinal))
        {
            throw new StageDirectApplyNotHeadOfDepartmentException();
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        var validation = await _validator.ValidateAsync(
            projectId,
            normalizedStageCode,
            validationTargetStatus,
            date,
            isHoD: true,
            ct);

        var validationErrors = validation.Errors
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToArray();

        if (validationErrors.Length > 0)
        {
            throw new StageDirectApplyValidationException(validationErrors, validation.MissingPredecessors);
        }

        var warnings = new List<string>(validation.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)));

        StageStatus targetStatus;
        if (isReopen)
        {
            targetStatus = date.HasValue ? StageStatus.InProgress : StageStatus.NotStarted;
        }
        else if (!Enum.TryParse(validationTargetStatus, ignoreCase: true, out targetStatus))
        {
            throw StageDirectApplyValidationException.FromMessages("The new status is not recognised.");
        }

        if (targetStatus == StageStatus.InProgress && !date.HasValue)
        {
            throw StageDirectApplyValidationException.FromMessages("Start date is required for InProgress.");
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

        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

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

        var missingPredecessors = validation.MissingPredecessors?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
            ?? Array.Empty<string>();

        var backfilledStages = new List<string>();

        if (missingPredecessors.Length > 0)
        {
            if (!forceBackfillPredecessors)
            {
                var details = validation.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
                if (details.Count == 0)
                {
                    details.Add("Complete required predecessor stages first.");
                }

                throw new StageDirectApplyValidationException(details, missingPredecessors);
            }

            var predecessors = await _db.ProjectStages
                .Where(ps => ps.ProjectId == projectId && missingPredecessors.Contains(ps.StageCode))
                .ToListAsync(ct);

            var predecessorLookup = predecessors
                .ToDictionary(ps => ps.StageCode, StringComparer.OrdinalIgnoreCase);

            foreach (var predecessorCode in missingPredecessors)
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
                predecessor.IsAutoCompleted = true;
                predecessor.AutoCompletedFromCode = stage.StageCode;
                predecessor.RequiresBackfill = true;

                backfilledStages.Add(predecessor.StageCode);

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
                    Note = string.Format(AutoBackfillNoteTemplate, stage.StageCode)
                };

                await _db.StageChangeLogs.AddAsync(autoBackfillLog, ct);
            }
        }

        bool adminCompletion = false;

        stage.IsAutoCompleted = false;
        stage.AutoCompletedFromCode = null;

        if (isReopen)
        {
            stage.CompletedOn = null;
            if (targetStatus == StageStatus.InProgress)
            {
                var startDate = date ?? today;
                stage.Status = StageStatus.InProgress;
                stage.ActualStart = startDate;
                stage.RequiresBackfill = false;
            }
            else
            {
                stage.Status = StageStatus.NotStarted;
                stage.ActualStart = null;
                stage.RequiresBackfill = false;
            }
        }
        else
        {
            switch (targetStatus)
            {
                case StageStatus.NotStarted:
                    stage.Status = StageStatus.NotStarted;
                    stage.ActualStart = null;
                    stage.CompletedOn = null;
                    stage.RequiresBackfill = false;
                    break;
                case StageStatus.InProgress:
                    stage.Status = StageStatus.InProgress;
                    stage.ActualStart = date!.Value;
                    stage.CompletedOn = null;
                    stage.RequiresBackfill = false;
                    break;
                case StageStatus.Completed:
                {
                    stage.Status = StageStatus.Completed;
                    if (!date.HasValue)
                    {
                        stage.ActualStart = null;
                        stage.CompletedOn = null;
                        adminCompletion = true;
                        stage.RequiresBackfill = true;
                        break;
                    }

                    var completionDate = date.Value;

                    if (!stage.ActualStart.HasValue && validation.SuggestedAutoStart.HasValue && completionDate >= validation.SuggestedAutoStart.Value)
                    {
                        stage.ActualStart = validation.SuggestedAutoStart.Value;
                    }

                    stage.CompletedOn = completionDate;

                    if (!stage.ActualStart.HasValue)
                    {
                        stage.RequiresBackfill = true;
                    }
                    else if (stage.CompletedOn.Value < stage.ActualStart.Value)
                    {
                        stage.CompletedOn = stage.ActualStart.Value;
                        stage.RequiresBackfill = false;
                        warnings.Add(ClampWarning);
                    }
                    else
                    {
                        stage.RequiresBackfill = false;
                    }

                    break;
                }
                case StageStatus.Blocked:
                    stage.Status = StageStatus.Blocked;
                    stage.RequiresBackfill = false;
                    break;
                case StageStatus.Skipped:
                    stage.Status = StageStatus.Skipped;
                    stage.RequiresBackfill = false;
                    break;
                default:
                    stage.Status = targetStatus;
                    stage.RequiresBackfill = false;
                    break;
            }
        }

        if (stage.Status == StageStatus.Completed && (!stage.ActualStart.HasValue || !stage.CompletedOn.HasValue))
        {
            stage.RequiresBackfill = true;
        }

        var finalStatus = stage.Status;
        var finalActualStart = stage.ActualStart;
        var finalCompletedOn = stage.CompletedOn;

        var logNote = CombineNotes(trimmedNote, adminCompletion ? AdminCompletionNote : null);

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
            Note = logNote
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
            Note = logNote
        };

        await _db.StageChangeLogs.AddAsync(directApplyLog, ct);
        await _db.StageChangeLogs.AddAsync(appliedLog, ct);
        await _db.SaveChangesAsync(ct);

        if (superseded)
        {
            warnings.Add("Pending request was superseded by this change.");
        }

        return new DirectApplyResult(
            finalStatus.ToString(),
            finalActualStart,
            finalCompletedOn,
            backfilledStages.Count,
            backfilledStages.ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? CombineNotes(string? primary, string? secondary)
    {
        if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(secondary))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(primary))
        {
            return secondary;
        }

        if (string.IsNullOrWhiteSpace(secondary))
        {
            return primary;
        }

        return string.Create(primary.Length + secondary.Length + 3, (primary, secondary), static (span, state) =>
        {
            var (p, s) = state;
            p.AsSpan().CopyTo(span);
            span[p.Length] = ' ';
            span[p.Length + 1] = '—';
            span[p.Length + 2] = ' ';
            s.AsSpan().CopyTo(span[(p.Length + 3)..]);
        });
    }
}

public sealed record DirectApplyResult(
    string UpdatedStatus,
    DateOnly? ActualStart,
    DateOnly? CompletedOn,
    int BackfilledCount,
    string[] BackfilledStages,
    string[] Warnings);

public sealed class StageDirectApplyNotFoundException : Exception
{
}

public sealed class StageDirectApplyNotHeadOfDepartmentException : Exception
{
}

public sealed class StageDirectApplyValidationException : Exception
{
    public StageDirectApplyValidationException(
        IEnumerable<string> details,
        IEnumerable<string>? missingPredecessors = null)
        : base("Stage direct apply validation failed.")
    {
        Details = details?.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToArray()
            ?? Array.Empty<string>();

        MissingPredecessors = missingPredecessors?.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray()
            ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Details { get; }
    public IReadOnlyList<string> MissingPredecessors { get; }

    public static StageDirectApplyValidationException FromMessages(params string[] messages)
        => new(messages ?? Array.Empty<string>(), Array.Empty<string>());
}
