using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Stages;

/// <summary>Where one stage stood on a past date, worked out from what is recorded today.</summary>
public enum StageStateAsOf
{
    NotStarted,
    InProgress,
    Finished,
    Skipped,
    /// <summary>Completed today, but nothing recorded says whether it was finished by the date.</summary>
    Unknown
}

/// <summary>One stage as stored today.</summary>
public sealed record StageFactNow(string Code, StageStatus Status, DateOnly? ActualStart, DateOnly? CompletedOn);

/// <summary>
/// One applied stage change (not a request): the status before and after, the completion dates it
/// carried, the day it was recorded (IST), and its place among the project's changes (recorded
/// time, then id), so two changes on one day keep their order.
/// </summary>
public sealed record StageLogFact(
    string Code,
    string? ToStatus,
    DateOnly RecordedOn,
    string? FromStatus = null,
    DateOnly? FromCompletedOn = null,
    DateOnly? ToCompletedOn = null,
    long Sequence = 0,
    DateOnly? FromActualStart = null,
    DateOnly? ToActualStart = null);

/// <summary>A project's stage on a past date: one stage, or the range it lies in when dates are missing.</summary>
public sealed record StagePositionAsOf(
    bool IsKnown,
    string? StageCode,
    string StageName,
    IReadOnlyList<ProjectLifecycleStageSnapshot> Candidates,
    bool IsConcluded)
{
    public static StagePositionAsOf None { get; } = new(false, null, "Stage unresolved", Array.Empty<ProjectLifecycleStageSnapshot>(), false);
}

/// <summary>
/// "Where was this project on date D" (owner, 6 Nov 2026: a past year's ARPP shows the position on
/// 31 March). Nothing is stored; every state comes from today's records, read in this order:
/// <list type="number">
/// <item>a stage's own dates (a completion on or before D is finished; after D, not);</item>
/// <item>its required predecessors: a stage cannot be finished while one of them was not;</item>
/// <item>the stage change log (applied changes only): the last change recorded on or before D is the
/// stage's status on D; a completion date the log carries decides as a recorded date does (so a stage
/// reopened after D keeps its earlier completion); and a stage, or one of its predecessors, recorded In
/// progress on or after D was not finished on D;</item>
/// <item>the stages after it: one that had started or finished by D means this one was finished; and,
/// since stages run back to back, a stage whose only undated predecessor is this one started the day
/// after this one ended;</item>
/// <item>otherwise the state is <see cref="StageStateAsOf.Unknown"/> and never guessed.</item>
/// </list>
/// Backfilled history (statuses set straight to Completed long after the fact) is only ever read as
/// "finished by the day it was recorded", never as "not finished before it".
/// </summary>
public static class StageStateAsOfResolver
{
    public static IReadOnlyDictionary<string, StageStateAsOf> Resolve(
        IReadOnlyList<StageFactNow> stages,
        IReadOnlyList<StageLogFact> logs,
        ProjectStageWorkflowSnapshot? workflow,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(logs);

        var byCode = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.Code))
            .GroupBy(stage => stage.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var logsByCode = logs
            .GroupBy(log => log.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> PredecessorsOf(string code) =>
            workflow?.RequiredPredecessorClosure(code) ?? Array.Empty<string>();
        IReadOnlyList<string> DirectPredecessorsOf(string code) =>
            workflow?.RequiredPredecessors(code) ?? Array.Empty<string>();
        var dependents = byCode.Keys.ToDictionary(
            code => code,
            code => byCode.Keys.Where(other => PredecessorsOf(other).Contains(code, StringComparer.OrdinalIgnoreCase)).ToList(),
            StringComparer.OrdinalIgnoreCase);
        var directDependents = byCode.Keys.ToDictionary(
            code => code,
            code => byCode.Keys.Where(other => DirectPredecessorsOf(other).Contains(code, StringComparer.OrdinalIgnoreCase)).ToList(),
            StringComparer.OrdinalIgnoreCase);

        bool Logged(string code, Func<StageLogFact, bool> match) =>
            logsByCode.TryGetValue(code, out var entries) && entries.Any(match);
        static bool IsStatus(StageLogFact log, StageStatus status) =>
            string.Equals(log.ToStatus, status.ToString(), StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<StageLogFact> HistoryOf(string code) =>
            logsByCode.TryGetValue(code, out var entries)
                ? entries.OrderBy(log => log.RecordedOn).ThenBy(log => log.Sequence).ToList()
                : Array.Empty<StageLogFact>();

        // Positive evidence that a stage had begun (or ended) by the date. A reset to Not started recorded by
        // then means it had not; otherwise a completion by then, a start date by then (today's, or one the
        // history carries for a start a later reset cleared; corrections applied), or a status recorded by then.
        bool StartedBy(string code)
        {
            if (!byCode.TryGetValue(code, out var fact))
            {
                return false;
            }

            var history = HistoryOf(code);
            var lastBefore = history.LastOrDefault(log => log.RecordedOn <= asOf);
            if (lastBefore is not null && IsStatus(lastBefore, StageStatus.NotStarted))
            {
                return false;
            }

            return fact.CompletedOn is { } end && end <= asOf
                || StartCycles(fact, history).Any(cycle => cycle.StartedOn(asOf))
                || lastBefore is not null && (IsStatus(lastBefore, StageStatus.InProgress)
                    || IsStatus(lastBefore, StageStatus.Completed)
                    || IsStatus(lastBefore, StageStatus.Blocked));
        }

        // Evidence that a stage was still open on the date: it, or a stage it needs, was recorded
        // In progress on or after it.
        bool RecordedOpenAfter(string code) =>
            new[] { code }.Concat(PredecessorsOf(code))
                .Any(candidate => Logged(candidate, log => log.RecordedOn >= asOf && IsStatus(log, StageStatus.InProgress)));

        var states = new Dictionary<string, StageStateAsOf>(StringComparer.OrdinalIgnoreCase);
        var ordered = byCode.Values
            .OrderBy(stage => workflow?.OrderOf(stage.Code) ?? 0)
            .ToList();

        // First pass: what each stage's own record and its predecessors say.
        foreach (var fact in ordered)
        {
            states[fact.Code] = OwnState(fact);
        }

        StageStateAsOf OwnState(StageFactNow fact)
        {
            StageStateAsOf Open() => StartedBy(fact.Code) ? StageStateAsOf.InProgress : StageStateAsOf.NotStarted;

            // Each completion, with any later date corrections applied, until the reopen that ended it.
            var cycles = CompletionCycles(fact, HistoryOf(fact.Code));
            if (cycles.Any(cycle => cycle.FinishedOn(asOf)))
            {
                return StageStateAsOf.Finished;
            }

            // Not finished on the date by any completion: the last change recorded by then gives its status.
            if (HistoryOf(fact.Code).LastOrDefault(log => log.RecordedOn <= asOf) is { } last
                && Enum.TryParse<StageStatus>(last.ToStatus, ignoreCase: true, out var then))
            {
                return then switch
                {
                    StageStatus.Skipped => StageStateAsOf.Skipped,
                    // Reset to (or still) Not started on the date: an earlier start was undone.
                    StageStatus.NotStarted => StageStateAsOf.NotStarted,
                    _ => Open()
                };
            }

            // A dated completion, but after the date (or reopened before it).
            if (cycles.Any(cycle => cycle.End.HasValue))
            {
                return Open();
            }

            switch (fact.Status)
            {
                case StageStatus.Skipped:
                    return StageStateAsOf.Skipped;
                case StageStatus.Completed:
                    break;
                default:
                    // Not finished today, and nothing says it was finished then.
                    return Open();
            }

            return RecordedOpenAfter(fact.Code) ? Open() : StageStateAsOf.Unknown;
        }

        // Dated evidence that a stage had begun or ended by the date (its own recorded start or completion,
        // not a log entry).
        bool DatedBy(string code) =>
            byCode.TryGetValue(code, out var fact)
            && (fact.ActualStart is { } start && start <= asOf || fact.CompletedOn is { } end && end <= asOf);

        // Second pass, repeated until nothing changes: settle the unknowns from the stages around them.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var fact in ordered)
            {
                var current = states[fact.Code];
                var next = current;

                // A stage cannot be finished while a stage it needs was not (a stage's own dates always win).
                if (current == StageStateAsOf.Unknown
                    && PredecessorsOf(fact.Code).Any(code => states.TryGetValue(code, out var state)
                        && state is StageStateAsOf.NotStarted or StageStateAsOf.InProgress))
                {
                    next = StartedBy(fact.Code) ? StageStateAsOf.InProgress : StageStateAsOf.NotStarted;
                }
                // A later stage with a dated start or completion by the date outweighs an older open-status
                // log: the stage (completed today, with no date of its own) was finished by then. Predecessor
                // backfill (completing earlier stages when a later one is entered) leaves exactly this shape.
                else if (current is StageStateAsOf.InProgress or StageStateAsOf.NotStarted
                    && fact.Status == StageStatus.Completed
                    && fact.CompletedOn is null
                    && dependents[fact.Code].Any(DatedBy))
                {
                    next = StageStateAsOf.Finished;
                }
                else if (current == StageStateAsOf.Unknown)
                {
                    // A later stage had begun or ended by the date: this one was finished.
                    if (dependents[fact.Code].Any(code => StartedBy(code)
                        || states.TryGetValue(code, out var state) && state == StageStateAsOf.Finished))
                    {
                        next = StageStateAsOf.Finished;
                    }
                    // Back to back: a following stage with a recorded start, whose only undated predecessor
                    // is this one, began the day after this one ended.
                    else if (EndFromNextStart(fact.Code) is { } end)
                    {
                        next = end <= asOf ? StageStateAsOf.Finished : StageStateAsOf.InProgress;
                    }
                }

                if (next != current)
                {
                    states[fact.Code] = next;
                    changed = true;
                }
            }
        }

        // The stages that follow this one directly, looking past skipped stages to the next effective one.
        IEnumerable<string> EffectiveFollowers(string code, HashSet<string> seen)
        {
            foreach (var follower in directDependents[code])
            {
                if (!seen.Add(follower))
                {
                    continue;
                }

                if (byCode.TryGetValue(follower, out var fact) && fact.Status == StageStatus.Skipped)
                {
                    foreach (var beyond in EffectiveFollowers(follower, seen))
                    {
                        yield return beyond;
                    }
                }
                else
                {
                    yield return follower;
                }
            }
        }

        // Every following stage that qualifies gives an end; parallel followers (TEC and BM after Bid) can
        // give different ones, and the stage had ended by the earliest of them.
        DateOnly? EndFromNextStart(string code)
        {
            DateOnly? earliest = null;
            foreach (var follower in EffectiveFollowers(code, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                if (!byCode.TryGetValue(follower, out var next) || next.ActualStart is not { } start)
                {
                    continue;
                }

                // The follower's other required predecessors (skipped ones looked past) must all have ended
                // before the day this one is taken to have ended.
                var others = DirectPredecessorsOf(follower)
                    .Where(other => !string.Equals(other, code, StringComparison.OrdinalIgnoreCase))
                    .Select(other => byCode.TryGetValue(other, out var fact) ? fact : null)
                    .Where(other => other is not null && other.Status != StageStatus.Skipped)
                    .ToList();
                if (others.All(other => other!.CompletedOn is { } otherEnd && otherEnd < start.AddDays(-1)))
                {
                    var end = start.AddDays(-1);
                    earliest = earliest is { } known && known <= end ? known : end;
                }
            }

            return earliest;
        }

        return states;
    }

    /// <summary>One start of a stage: its start date (after any corrections) and when a reset to Not started ended it.</summary>
    private sealed record StartCycle(DateOnly? Start, DateOnly? ResetOn)
    {
        public bool StartedOn(DateOnly asOf) =>
            Start is { } start && start <= asOf && (ResetOn is not { } reset || reset > asOf);
    }

    // Reads a stage's applied history as start cycles. A change carrying a start date sets or corrects the
    // cycle's start; a reset to Not started closes it. Today's record is the last cycle, with today's start
    // date authoritative for it.
    private static IReadOnlyList<StartCycle> StartCycles(StageFactNow fact, IReadOnlyList<StageLogFact> history)
    {
        var cycles = new List<StartCycle>();
        DateOnly? start = null;
        foreach (var log in history)
        {
            var reset = string.Equals(log.ToStatus, nameof(StageStatus.NotStarted), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(log.FromStatus, nameof(StageStatus.NotStarted), StringComparison.OrdinalIgnoreCase);
            if (reset)
            {
                cycles.Add(new StartCycle(start ?? log.FromActualStart, log.RecordedOn));
                start = null;
                continue;
            }

            if (log.ToActualStart is { } corrected)
            {
                start = corrected;
            }
            else if (start is null && log.FromActualStart is { } earlier)
            {
                start = earlier;
            }
        }

        cycles.Add(new StartCycle(fact.ActualStart ?? (fact.Status == StageStatus.NotStarted ? null : start), null));
        return cycles;
    }

    /// <summary>One completion of a stage: its end (after any date corrections), when it was recorded, and when a reopen ended it.</summary>
    private sealed record CompletionCycle(DateOnly? End, DateOnly? RecordedOn, DateOnly? ReopenedOn)
    {
        /// <summary>Finished on the date: completed by then (dated, or recorded by then when undated) and not yet reopened.</summary>
        public bool FinishedOn(DateOnly asOf) =>
            (End is { } end ? end <= asOf : RecordedOn is { } recorded && recorded <= asOf)
            && (ReopenedOn is not { } reopened || reopened > asOf);
    }

    // Reads a stage's applied history as completion cycles. A completion opens a cycle; a later change that
    // stays Completed only corrects its date; a change away from Completed (a reopen) closes it. Today's
    // record is the last cycle, with today's completion date authoritative for it.
    private static IReadOnlyList<CompletionCycle> CompletionCycles(StageFactNow fact, IReadOnlyList<StageLogFact> history)
    {
        static bool Is(string? status, StageStatus expected) =>
            string.Equals(status, expected.ToString(), StringComparison.OrdinalIgnoreCase);

        var cycles = new List<CompletionCycle>();
        var open = false;
        DateOnly? end = null;
        DateOnly? recorded = null;
        foreach (var log in history)
        {
            if (Is(log.ToStatus, StageStatus.Completed))
            {
                if (!open)
                {
                    open = true;
                    recorded = log.RecordedOn;
                    end = log.ToCompletedOn;
                }
                else if (log.ToCompletedOn is { } corrected)
                {
                    end = corrected;
                }
            }
            else if (open || Is(log.FromStatus, StageStatus.Completed))
            {
                cycles.Add(new CompletionCycle(end ?? log.FromCompletedOn, open ? recorded : null, log.RecordedOn));
                open = false;
                end = null;
                recorded = null;
            }
        }

        if (fact.Status == StageStatus.Completed)
        {
            cycles.Add(new CompletionCycle(fact.CompletedOn ?? end, open ? recorded : null, null));
        }

        return cycles;
    }

    /// <summary>
    /// The stage on the date, by the same rule as today's current stage
    /// (<see cref="ProjectLifecyclePositionResolver"/>). An unknown stage is tried both ways: when both
    /// give the same stage it is known; otherwise the stages between them are the candidates.
    /// </summary>
    public static StagePositionAsOf ResolvePosition(
        IReadOnlyList<ProjectLifecycleStageSnapshot> workflowStages,
        IReadOnlyDictionary<string, StageStateAsOf> states)
    {
        ArgumentNullException.ThrowIfNull(workflowStages);
        ArgumentNullException.ThrowIfNull(states);
        if (workflowStages.Count == 0)
        {
            return StagePositionAsOf.None;
        }

        ProjectLifecyclePositionSnapshot Run(bool unknownFinished) =>
            ProjectLifecyclePositionResolver.Resolve(workflowStages.Select(stage => stage with
            {
                Status = (states.TryGetValue(stage.Code, out var state) ? state : StageStateAsOf.NotStarted) switch
                {
                    StageStateAsOf.Finished => StageStatus.Completed,
                    StageStateAsOf.Skipped => StageStatus.Skipped,
                    StageStateAsOf.InProgress => StageStatus.InProgress,
                    StageStateAsOf.Unknown => unknownFinished ? StageStatus.Completed : StageStatus.InProgress,
                    _ => StageStatus.NotStarted
                }
            }));

        var earliest = Run(unknownFinished: false);
        var latest = Run(unknownFinished: true);
        var from = earliest.CurrentStage;
        var to = latest.CurrentStage;

        if (from is null && to is null)
        {
            return new StagePositionAsOf(true, null, "Concluded", Array.Empty<ProjectLifecycleStageSnapshot>(), true);
        }

        if (from is not null && to is not null && string.Equals(from.Code, to.Code, StringComparison.OrdinalIgnoreCase))
        {
            return new StagePositionAsOf(true, from.Code, from.Name, new[] { from }, false);
        }

        // The resolver takes an in-progress stage first, so with parallel stages (TEC and BM) the two runs
        // can land either way round: the candidates are everything between them.
        var low = from is null || to is null ? from?.SortOrder ?? int.MinValue : Math.Min(from.SortOrder, to.SortOrder);
        var high = from is null || to is null ? to?.SortOrder ?? int.MaxValue : Math.Max(from.SortOrder, to.SortOrder);
        var candidates = workflowStages
            .Where(stage => stage.SortOrder >= low && stage.SortOrder <= high)
            .OrderBy(stage => stage.SortOrder)
            .ToList();
        var name = candidates.Count switch
        {
            0 => "Stage not known",
            1 => candidates[0].Name,
            2 => $"{candidates[0].Name} or {candidates[1].Name}",
            _ => $"{candidates[0].Name} to {candidates[^1].Name}"
        };
        return new StagePositionAsOf(false, null, name, candidates, to is null);
    }
}
