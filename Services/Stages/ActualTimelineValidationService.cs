using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Source-neutral validation of Actual start / Actual completion corrections
/// against the complete final proposed state of a project's lifecycle.
/// Direct HoD/Admin corrections, completion backfill and (later) approved PO
/// proposals all evaluate the same rules; only authorization differs.
/// Validation never mutates stored stages.
/// </summary>
public interface IActualTimelineValidationService
{
    Task<ActualTimelineValidationResult> ValidateAsync(
        ActualTimelineCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class ActualTimelineValidationService : IActualTimelineValidationService
{
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;
    private readonly IClock _clock;

    public ActualTimelineValidationService(
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService arppIpaStageAuthority,
        IClock clock)
    {
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? throw new ArgumentNullException(nameof(arppIpaStageAuthority));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ActualTimelineValidationResult> ValidateAsync(
        ActualTimelineCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // Callers on publication paths invoke this inside their transaction after
        // locking, so workflow and ARPP authority are read from the same snapshot.
        var workflow = await _workflowPolicy.GetAsync(candidate.ProjectId, cancellationToken);
        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(candidate.ProjectId, cancellationToken);
        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);

        return Evaluate(workflow, ipaAuthority, today, candidate);
    }

    // SECTION: Evaluation
    internal static ActualTimelineValidationResult Evaluate(
        ProjectStageWorkflowSnapshot workflow,
        ArppIpaStageAuthority? ipaAuthority,
        DateOnly today,
        ActualTimelineCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(candidate);

        var official = (candidate.Official ?? Array.Empty<ActualStageState>())
            .Where(stage => stage is not null && !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var final = new Dictionary<string, ActualStageState>(official, StringComparer.OrdinalIgnoreCase);
        var errors = new List<ActualValidationIssue>();
        var changes = new List<ActualStageResolution>();

        // The last submission for a stage wins, matching the existing editors.
        var requested = (candidate.Changes ?? Array.Empty<ActualStageChange>())
            .Where(change => change is not null && !string.IsNullOrWhiteSpace(change.StageCode))
            .GroupBy(change => change.StageCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last());

        foreach (var change in requested)
        {
            var code = change.StageCode.Trim();
            if (!official.TryGetValue(code, out var stage))
            {
                errors.Add(new ActualValidationIssue(
                    code,
                    ActualValidationRule.UnknownStage,
                    $"{code}: This stage was not found for this project."));
                continue;
            }

            var projected = candidate.EntryPoint == ActualChangeEntryPoint.Backfill
                ? ProjectBackfill(stage, change, ipaAuthority, today, errors)
                : ProjectCorrection(stage, change, ipaAuthority, today, errors);

            if (projected is null || IsSameFacts(projected, stage))
            {
                continue;
            }

            final[stage.StageCode] = projected;
            changes.Add(new ActualStageResolution(
                stage.StageCode,
                stage,
                projected,
                // Adopting a published ARPP completion on a stale IPA row is ARPP
                // reconciliation, not a user-supplied completion backfill.
                IsBackfill: !IsManagedIpa(stage.StageCode, ipaAuthority)
                    && stage.Status == StageStatus.Completed
                    && !stage.CompletedOn.HasValue
                    && projected.CompletedOn.HasValue));
        }

        var changedCodes = changes
            .Select(change => change.StageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var officialViolations = FindViolations(workflow, official, ipaAuthority).ToHashSet();
        var finalBounds = new Dictionary<string, IReadOnlyList<StageChronologyBoundary>>(StringComparer.OrdinalIgnoreCase);
        var finalViolations = FindViolations(workflow, final, ipaAuthority, finalBounds).ToArray();
        var stageNames = workflow.Stages
            .GroupBy(stage => stage.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

        // A violation identical to one already present in the official record is
        // historical data, not something this correction introduced: it is reported
        // but does not block unrelated work. Any violation that depends on a changed
        // date is new and blocks.
        foreach (var group in finalViolations
                     .Where(violation => !officialViolations.Contains(violation))
                     .GroupBy(violation => violation.StageCode, StringComparer.OrdinalIgnoreCase))
        {
            var controlling = Controlling(group);
            errors.Add(new ActualValidationIssue(
                controlling.StageCode,
                ActualValidationRule.Chronology,
                changedCodes.Contains(controlling.StageCode)
                    ? ChangedStageMessage(controlling, stageNames)
                    : AffectedSuccessorMessage(controlling, stageNames, ipaAuthority),
                controlling.SourceStageCode));
        }

        var warnings = finalViolations
            .Where(officialViolations.Contains)
            .GroupBy(violation => violation.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var controlling = Controlling(group);
                return new ActualValidationIssue(
                    controlling.StageCode,
                    ActualValidationRule.Chronology,
                    ExistingViolationMessage(controlling, stageNames),
                    controlling.SourceStageCode);
            })
            .ToArray();

        var affected = new HashSet<string>(changedCodes, StringComparer.OrdinalIgnoreCase);
        foreach (var (stageCode, bounds) in finalBounds)
        {
            if (bounds.Any(bound => changedCodes.Contains(bound.SourceStageCode)))
            {
                affected.Add(stageCode);
            }
        }

        return new ActualTimelineValidationResult(
            errors,
            warnings,
            changes,
            InWorkflowOrder(final.Values, workflow, state => state.StageCode).ToArray(),
            InWorkflowOrder(affected, workflow, code => code).ToArray());
    }

    // SECTION: Per-row projection (generic correction)
    private static ActualStageState? ProjectCorrection(
        ActualStageState stage,
        ActualStageChange change,
        ArppIpaStageAuthority? ipaAuthority,
        DateOnly today,
        ICollection<ActualValidationIssue> errors)
    {
        var code = stage.StageCode;
        var isManagedIpa = IsManagedIpa(code, ipaAuthority);
        var effectiveStatus = StageEffectiveState.Resolve(
            code,
            new StageChronologyState(stage.Status, stage.CompletedOn),
            ipaAuthority)!.Status;

        if (isManagedIpa && change.CompletedOn.HasValue && change.CompletedOn != ipaAuthority!.IssueDate)
        {
            errors.Add(Issue(code, ActualValidationRule.PublishedArppCompletion,
                $"{code}: Completion date is controlled by the published ARPP record and cannot be edited here."));
            return null;
        }

        if (change.ClearActualStart && change.ActualStart.HasValue)
        {
            errors.Add(Issue(code, ActualValidationRule.ConflictingStartIntent,
                $"{code}: Supply a start date or explicitly clear it, not both."));
            return null;
        }

        if (change.ClearActualStart && effectiveStatus == StageStatus.InProgress)
        {
            errors.Add(Issue(code, ActualValidationRule.StartRequired,
                $"{code}: Actual start is required while the stage is InProgress. Transition the stage before clearing it."));
            return null;
        }

        // A missing value is not a clear command, including for managed IPA.
        var proposedStart = change.ClearActualStart ? null : change.ActualStart ?? stage.ActualStart;
        var startChanged = proposedStart != stage.ActualStart;

        // A requested start correction on managed IPA carries the published
        // completion, even when the stored value is stale. Unchanged rows are not
        // an implicit reconciliation job.
        var proposedCompleted = isManagedIpa
            ? (startChanged ? ipaAuthority!.IssueDate : stage.CompletedOn)
            : change.CompletedOn ?? stage.CompletedOn;

        if (!startChanged && proposedCompleted == stage.CompletedOn)
        {
            return stage;
        }

        if (effectiveStatus is not StageStatus.InProgress and not StageStatus.Completed)
        {
            errors.Add(Issue(code, ActualValidationRule.StatusNotEditable,
                $"{code}: Actual dates can only be edited when the stage is InProgress or Completed."));
            return null;
        }

        if (effectiveStatus == StageStatus.InProgress)
        {
            if (!proposedStart.HasValue)
            {
                errors.Add(Issue(code, ActualValidationRule.StartRequired,
                    $"{code}: Actual start is required while the stage is InProgress."));
            }

            if (proposedCompleted.HasValue)
            {
                errors.Add(Issue(code, ActualValidationRule.CompletionNotAllowed,
                    $"{code}: Use the stage-transition workflow to complete this stage; an InProgress stage cannot carry a completion date."));
            }
        }

        if (proposedStart > today)
        {
            errors.Add(Issue(code, ActualValidationRule.FutureStart,
                $"{code}: Start date cannot be in the future."));
        }

        if (proposedCompleted > today)
        {
            errors.Add(Issue(code, ActualValidationRule.FutureCompletion,
                $"{code}: Completion date cannot be in the future."));
        }

        if (proposedStart.HasValue && proposedCompleted.HasValue && proposedStart > proposedCompleted)
        {
            errors.Add(Issue(code, ActualValidationRule.StartAfterCompletion,
                $"{code}: Completion date must be on or after the start date."));
        }

        // An explicit correction publishes the effective lifecycle state (this is
        // how a stale stored IPA row adopts published ARPP completion).
        var requiresBackfill = effectiveStatus == StageStatus.Completed
            ? !isManagedIpa && !proposedCompleted.HasValue
            : stage.RequiresBackfill;

        return new ActualStageState(code, effectiveStatus, proposedStart, proposedCompleted, requiresBackfill);
    }

    // SECTION: Per-row projection (mandatory completion backfill)
    private static ActualStageState? ProjectBackfill(
        ActualStageState stage,
        ActualStageChange change,
        ArppIpaStageAuthority? ipaAuthority,
        DateOnly today,
        ICollection<ActualValidationIssue> errors)
    {
        var code = stage.StageCode;

        if (IsManagedIpa(code, ipaAuthority))
        {
            errors.Add(Issue(code, ActualValidationRule.PublishedArppCompletion, ArppManagedIpaStageException.UserMessage));
            return null;
        }

        if (stage.Status != StageStatus.Completed || stage.CompletedOn.HasValue)
        {
            errors.Add(Issue(code, ActualValidationRule.NotAwaitingBackfill,
                $"Stage {code}: this stage no longer requires a completion date."));
            return null;
        }

        if (change.ClearActualStart)
        {
            errors.Add(Issue(code, ActualValidationRule.ConflictingStartIntent,
                $"Stage {code}: an actual start cannot be cleared while supplying a completion date."));
            return null;
        }

        var start = change.ActualStart ?? stage.ActualStart;
        var completed = change.CompletedOn ?? stage.CompletedOn;

        if (start > today)
        {
            errors.Add(Issue(code, ActualValidationRule.FutureStart,
                $"Stage {code}: actual start date cannot be in the future."));
        }

        if (!completed.HasValue)
        {
            errors.Add(Issue(code, ActualValidationRule.CompletionRequired,
                $"Stage {code}: completion date is required."));
        }
        else if (completed > today)
        {
            errors.Add(Issue(code, ActualValidationRule.FutureCompletion,
                $"Stage {code}: completion date cannot be in the future."));
        }

        if (start.HasValue && completed.HasValue && start > completed)
        {
            errors.Add(Issue(code, ActualValidationRule.StartAfterCompletion,
                $"Stage {code}: completion date must be on or after the start date."));
        }

        return new ActualStageState(code, StageStatus.Completed, start, completed, RequiresBackfill: false);
    }

    // SECTION: Graph chronology
    private static IEnumerable<ChronologyViolation> FindViolations(
        ProjectStageWorkflowSnapshot workflow,
        IReadOnlyDictionary<string, ActualStageState> states,
        ArppIpaStageAuthority? ipaAuthority,
        IDictionary<string, IReadOnlyList<StageChronologyBoundary>>? boundsByStage = null)
    {
        StageChronologyState? Accessor(string code) =>
            states.TryGetValue(code, out var state)
                ? new StageChronologyState(state.Status, state.CompletedOn)
                : null;

        foreach (var state in states.Values)
        {
            // A retained PNC row is outside the effective graph while PNC is not
            // applicable: its stale dates neither bound nor are bounded by others.
            if (!workflow.PncApplicable
                && string.Equals(state.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var effective = StageEffectiveState.Resolve(
                state.StageCode,
                new StageChronologyState(state.Status, state.CompletedOn),
                ipaAuthority)!;

            if (effective.Status is not StageStatus.InProgress and not StageStatus.Completed and not StageStatus.Blocked)
            {
                continue;
            }

            // An explicit start is the chronology fact; a completed stage with an
            // unknown start is bounded by its completion, as the editors do today.
            var isStart = state.ActualStart.HasValue;
            var date = state.ActualStart
                ?? (effective.Status == StageStatus.Completed ? effective.CompletionDate : null);
            if (!date.HasValue)
            {
                continue;
            }

            var bounds = StageDateSuggestionResolver.ResolveBranchBoundaries(
                workflow,
                state.StageCode,
                Accessor,
                ipaAuthority);
            if (boundsByStage is not null)
            {
                boundsByStage[state.StageCode] = bounds;
            }

            foreach (var bound in bounds)
            {
                if (date.Value < bound.CompletionDate)
                {
                    yield return new ChronologyViolation(
                        state.StageCode,
                        isStart,
                        date.Value,
                        bound.SourceStageCode,
                        bound.CompletionDate);
                }
            }
        }
    }

    private static ChronologyViolation Controlling(IEnumerable<ChronologyViolation> violations) =>
        violations
            .OrderByDescending(violation => violation.SourceCompletionDate.DayNumber)
            .ThenBy(violation => violation.SourceStageCode, StringComparer.OrdinalIgnoreCase)
            .First();

    // SECTION: Messages
    private static string ChangedStageMessage(
        ChronologyViolation violation,
        IReadOnlyDictionary<string, string> stageNames) =>
        $"{violation.StageCode}: {(violation.IsStart ? "Start date" : "Completion date")} must be on or after " +
        $"{Display(violation.SourceCompletionDate)}, when {NameOf(violation.SourceStageCode, stageNames)} was completed. " +
        "Same-day commencement is permitted.";

    private static string AffectedSuccessorMessage(
        ChronologyViolation violation,
        IReadOnlyDictionary<string, string> stageNames,
        ArppIpaStageAuthority? ipaAuthority)
    {
        var message =
            $"{violation.StageCode}: Actual {(violation.IsStart ? "start" : "completion")} {Display(violation.Date)} " +
            $"would be before {NameOf(violation.SourceStageCode, stageNames)} Actual completion " +
            $"{Display(violation.SourceCompletionDate)}. Same-day commencement is permitted. " +
            $"Correct {violation.StageCode} in the same update.";

        // A published ARPP completion cannot be corrected here; say where to fix it.
        return IsManagedIpa(violation.StageCode, ipaAuthority)
            ? message + " Its completion date is controlled by the published ARPP record."
            : message;
    }

    private static string ExistingViolationMessage(
        ChronologyViolation violation,
        IReadOnlyDictionary<string, string> stageNames) =>
        $"{violation.StageCode}: The recorded Actual {(violation.IsStart ? "start" : "completion")} " +
        $"{Display(violation.Date)} is already before {NameOf(violation.SourceStageCode, stageNames)} " +
        $"Actual completion {Display(violation.SourceCompletionDate)}. This existing record was not changed by this update.";

    private static string Display(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    // SECTION: Helpers
    private static ActualValidationIssue Issue(string code, ActualValidationRule rule, string message) =>
        new(code, rule, message);

    private static bool IsManagedIpa(string? stageCode, ArppIpaStageAuthority? ipaAuthority) =>
        ipaAuthority is not null && string.Equals(stageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameFacts(ActualStageState left, ActualStageState right) =>
        left.Status == right.Status
        && left.ActualStart == right.ActualStart
        && left.CompletedOn == right.CompletedOn
        && left.RequiresBackfill == right.RequiresBackfill;

    private static string NameOf(string code, IReadOnlyDictionary<string, string> stageNames) =>
        stageNames.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name) ? name : code;

    private static IEnumerable<T> InWorkflowOrder<T>(
        IEnumerable<T> items,
        ProjectStageWorkflowSnapshot workflow,
        Func<T, string> codeOf) =>
        items
            .OrderBy(item => workflow.OrderOf(codeOf(item)))
            .ThenBy(item => codeOf(item), StringComparer.OrdinalIgnoreCase);

    private sealed record ChronologyViolation(
        string StageCode,
        bool IsStart,
        DateOnly Date,
        string SourceStageCode,
        DateOnly SourceCompletionDate);
}

// SECTION: Candidate and result contracts
/// <summary>The form a correction was entered through. Backfill supplies a missing completion date.</summary>
public enum ActualChangeEntryPoint
{
    Editor,
    Backfill
}

/// <summary>Stored stage facts used as the official baseline.</summary>
public sealed record ActualStageState(
    string StageCode,
    StageStatus Status,
    DateOnly? ActualStart,
    DateOnly? CompletedOn,
    bool RequiresBackfill)
{
    public static ActualStageState From(ProjectStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return new ActualStageState(stage.StageCode, stage.Status, stage.ActualStart, stage.CompletedOn, stage.RequiresBackfill);
    }
}

/// <summary>
/// A requested correction. A null date means "keep the recorded value";
/// clearing an Actual start requires <see cref="ClearActualStart"/>.
/// </summary>
public sealed record ActualStageChange(
    string StageCode,
    DateOnly? ActualStart = null,
    bool ClearActualStart = false,
    DateOnly? CompletedOn = null);

/// <summary>
/// The complete official stage set for one project plus the requested changes.
/// Callers that publish pass rows read under their transaction and lock.
/// </summary>
public sealed record ActualTimelineCandidate(
    int ProjectId,
    IReadOnlyList<ActualStageState> Official,
    IReadOnlyList<ActualStageChange> Changes,
    ActualChangeEntryPoint EntryPoint = ActualChangeEntryPoint.Editor);

public enum ActualValidationRule
{
    UnknownStage,
    PublishedArppCompletion,
    ConflictingStartIntent,
    StatusNotEditable,
    StartRequired,
    CompletionNotAllowed,
    FutureStart,
    FutureCompletion,
    StartAfterCompletion,
    NotAwaitingBackfill,
    CompletionRequired,
    Chronology
}

public sealed record ActualValidationIssue(
    string StageCode,
    ActualValidationRule Rule,
    string Message,
    string? RelatedStageCode = null);

/// <summary>A stage whose facts would change, with its official and final values.</summary>
public sealed record ActualStageResolution(
    string StageCode,
    ActualStageState Official,
    ActualStageState Final,
    bool IsBackfill);

public sealed record ActualTimelineValidationResult(
    IReadOnlyList<ActualValidationIssue> Errors,
    IReadOnlyList<ActualValidationIssue> Warnings,
    IReadOnlyList<ActualStageResolution> Changes,
    IReadOnlyList<ActualStageState> Final,
    IReadOnlyList<string> AffectedStageCodes)
{
    public bool IsValid => Errors.Count == 0;
}
