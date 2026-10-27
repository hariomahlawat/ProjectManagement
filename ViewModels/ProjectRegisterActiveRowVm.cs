using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.ViewModels;

/// <summary>
/// Display text for one Active register row (docs/ux/projects-register.md). Every value is a
/// projection of <see cref="ProjectRegisterActiveFacts"/>; nothing here decides a fact.
/// </summary>
public sealed record ProjectRegisterActiveRowVm(
    IReadOnlyList<ProjectRegisterStageMarkVm> Marks,
    string TrackerLabel,
    string StageName,
    string StateText,
    string StateTone,
    string AgeText,
    string TimingText,
    string TimingTone,
    int? BarFillPercent,
    int? BarMarkPercent,
    string PdcText,
    string? PlannedText,
    string UpdatedText,
    bool IsStale)
{
    public static ProjectRegisterActiveRowVm Create(ProjectRegisterActiveFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var marks = facts.Stages
            .Select(stage => new ProjectRegisterStageMarkVm(
                MarkModifier(stage.State),
                $"{stage.Name}: {MarkLabel(stage.State)}"))
            .ToList();

        var (stateText, stateTone) = facts.CurrentStageState switch
        {
            ProjectRegisterStageState.InProgress => (Since("In progress", facts.StageAgeFrom), string.Empty),
            ProjectRegisterStageState.Blocked => (Since("Blocked", facts.StageAgeFrom), "blocked"),
            ProjectRegisterStageState.Finished => ("All stages complete", string.Empty),
            _ => (NotStartedText(facts), "waiting")
        };

        var ageText = facts.DaysInStage is int days
            ? facts.CurrentStageState == ProjectRegisterStageState.NotStarted ? $"{days} d waiting" : $"{days} d"
            : "—";

        var (timingText, timingTone) = facts.PlanStatus switch
        {
            ProjectRegisterPlanStatus.Late => ($"{facts.DaysLate} d late", facts.IsBlocked ? "blocked" : "late"),
            ProjectRegisterPlanStatus.OnPlan => (facts.DaysLeft == 0 ? "Due today" : $"{facts.DaysLeft} d left", facts.IsBlocked ? "blocked" : "ok"),
            _ => ("No PDC", facts.IsBlocked ? "blocked" : "none")
        };

        int? fill = null;
        int? mark = null;
        if (facts.PlannedDays is int planned && facts.DaysInStage is int inStage)
        {
            var track = Math.Max(1, Math.Max(planned, inStage));
            fill = (int)Math.Round(inStage * 100.0 / track);
            mark = (int)Math.Round(planned * 100.0 / track);
        }

        return new ProjectRegisterActiveRowVm(
            marks,
            $"Stage {facts.CurrentStageIndex + 1} of {marks.Count}",
            facts.CurrentStageName,
            stateText,
            stateTone,
            ageText,
            timingText,
            timingTone,
            fill,
            mark,
            facts.StagePdc is { } pdc ? FormatDate(pdc) : "Not set",
            facts.PlannedDays is int plannedDays ? $"Planned {plannedDays} d" : null,
            DaysAgo(facts.DaysSinceUpdate),
            facts.IsStale);
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        return string.Concat(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));
    }

    private static string NotStartedText(ProjectRegisterActiveFacts facts)
    {
        var previous = facts.Stages
            .Take(facts.CurrentStageIndex)
            .LastOrDefault(stage => stage.State == ProjectRegisterStageMarkState.Done);

        return previous is not null && facts.StageAgeFrom is { } done
            ? $"Not started · {previous.Name} done {FormatDate(done)}"
            : "Not started";
    }

    private static string Since(string state, DateOnly? from)
        => from is { } date ? $"{state} · since {FormatDate(date)}" : state;

    private static string DaysAgo(int days) => days switch
    {
        0 => "Today",
        1 => "Yesterday",
        _ => $"{days} d ago"
    };

    private static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string MarkModifier(ProjectRegisterStageMarkState state) => state switch
    {
        ProjectRegisterStageMarkState.Done => "done",
        ProjectRegisterStageMarkState.Skipped => "skipped",
        ProjectRegisterStageMarkState.Current => "current",
        ProjectRegisterStageMarkState.Blocked => "blocked",
        ProjectRegisterStageMarkState.Waiting => "waiting",
        _ => "upcoming"
    };

    private static string MarkLabel(ProjectRegisterStageMarkState state) => state switch
    {
        ProjectRegisterStageMarkState.Done => "completed",
        ProjectRegisterStageMarkState.Skipped => "skipped",
        ProjectRegisterStageMarkState.Current => "in progress",
        ProjectRegisterStageMarkState.Blocked => "blocked",
        ProjectRegisterStageMarkState.Waiting => "not started",
        _ => "upcoming"
    };
}

/// <summary>One dot in the stage tracker: its style modifier and its hover text.</summary>
public sealed record ProjectRegisterStageMarkVm(string Modifier, string Title);
