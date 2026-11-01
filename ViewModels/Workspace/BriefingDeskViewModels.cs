namespace ProjectManagement.ViewModels.Workspace;

// SECTION: Briefing desk
// The Project Officer landing page. Every assigned project, idea and task is shown as a
// card that answers one question: is this item ready to be briefed in conference?
public sealed class BriefingDeskVm
{
    public int ReadyCount { get; init; }

    public int TotalCount { get; init; }

    public IReadOnlyList<BriefingDeskSectionVm> Sections { get; init; } = Array.Empty<BriefingDeskSectionVm>();

    /// <summary>Completed projects still shown in conference review; not counted as work.</summary>
    public IReadOnlyList<BriefingDeskItemVm> RecentlyCompleted { get; init; } = Array.Empty<BriefingDeskItemVm>();

    public IReadOnlyList<BriefingDeskWaitingVm> WithOthers { get; init; } = Array.Empty<BriefingDeskWaitingVm>();


    public int NotReadyCount => TotalCount - ReadyCount;
}

public sealed class BriefingDeskSectionVm
{
    public ConferenceItemKind Kind { get; init; }

    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<BriefingDeskItemVm> Items { get; init; } = Array.Empty<BriefingDeskItemVm>();

    public int ReadyCount => Items.Count(item => item.IsBriefReady);
}

public sealed class BriefingDeskItemVm
{
    public ConferenceItemKind Kind { get; init; }

    public int ItemId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string OpenUrl { get; init; } = string.Empty;

    public string KindLabel => BriefingDeskLabels.KindLabel(Kind);

    /// <summary>Stable fragment id, also used as the focus key after an inline post.</summary>
    public string AnchorId => $"desk-{BriefingDeskLabels.KindKey(Kind)}-{ItemId}";

    public bool IsBriefReady => Checks.Where(check => check.IsRequired).All(check => check.State != BriefingDeskCheckState.Open);

    public int OpenCount => Checks.Count(check => check.IsRequired && check.State == BriefingDeskCheckState.Open);

    /// <summary>
    /// True when the update box answers an open step (the weekly update or a direction). When
    /// false the PO may still post, but the card says no update is needed.
    /// </summary>
    public bool IsUpdateDue => Checks.Any(check => check.Route == BriefingDeskRoute.Update
        && (check.State == BriefingDeskCheckState.Open || check.IsDueSoon));

    /// <summary>
    /// The current stage the remark box offers to confirm ("Stage is still DEVP"). Null for
    /// ideas, tasks, completed projects and a project with no current stage.
    /// </summary>
    public BriefingDeskStageVm? StageToConfirm { get; init; }

    public IReadOnlyList<BriefingDeskFactVm> Facts { get; init; } = Array.Empty<BriefingDeskFactVm>();

    public IReadOnlyList<BriefingDeskStepVm> Strip { get; init; } = Array.Empty<BriefingDeskStepVm>();

    public ConferenceDirectionVm? Direction { get; init; }

    public bool DirectionAwaitingProgress { get; init; }

    public string LastUpdateText { get; init; } = string.Empty;

    public IReadOnlyList<BriefingDeskCheckVm> Checks { get; init; } = Array.Empty<BriefingDeskCheckVm>();

    /// <summary>Record-health improvements: shown with their points, never required for readiness.</summary>
    public IReadOnlyList<BriefingDeskCheckVm> Improvements { get; init; } = Array.Empty<BriefingDeskCheckVm>();

    public int? RecordHealthPercent { get; init; }

    public int ImprovementPoints { get; init; }

    /// <summary>Procurement editor shown in a panel on the desk; null when not needed.</summary>
    public ProjectManagement.ViewModels.ProcurementEditVm? ProcurementEdit { get; init; }

    public string? ProcurementPanelId { get; init; }

    /// <summary>Short current-stage plan form; null when the full editor is needed.</summary>
    public BriefingDeskPlanPanelVm? PlanPanel { get; init; }

    public string? TaskRowVersion { get; init; }

    public bool CanSubmitTask { get; init; }

    public bool CanFlagTaskBlocked { get; init; }

    /// <summary>The same item as the Conference view shows it to the Comdt.</summary>
    public OfficerConferenceItemVm? Conference { get; init; }

    public string NextStepText
    {
        get
        {
            var open = Checks.Where(check => check.IsRequired && check.State == BriefingDeskCheckState.Open).ToList();
            if (open.Count == 0)
            {
                return "Nothing outstanding before the conference.";
            }

            // One open step reads in full ("Set DEVP PDC"); several are named in order, briefly
            // ("Remark · Set DEVP PDC · Procurement"), so a folded card says what is left.
            return open.Count == 1
                ? open[0].Label
                : string.Join(" · ", open.Select(check => check.NextText ?? check.Label));
        }
    }
}

public enum BriefingDeskTone
{
    Neutral,
    Good,
    Warning,
    Danger
}

/// <summary>
/// A header tag on a card. <paramref name="Url"/> makes it a link to where it is fixed.
/// <paramref name="CoveredByStep"/> marks a tag that a step on the open card repeats, so the open
/// card shows it once, as the step. <paramref name="Key"/> names what the tag is about
/// (<see cref="BriefingDeskFactKeys"/>), so other views can place it without reading its wording.
/// </summary>
public sealed record BriefingDeskFactVm(string Text, BriefingDeskTone Tone, string? Url = null, bool CoveredByStep = false, string? Key = null);

public static class BriefingDeskFactKeys
{
    public const string Stage = "stage";
    public const string Pdc = "pdc";
    public const string Remark = "remark";
}

public enum BriefingDeskStepState
{
    Past,
    Current,
    Future,
    Blocked
}

public sealed record BriefingDeskStepVm(string Label, string Title, BriefingDeskStepState State);

public enum BriefingDeskCheckState
{
    /// <summary>The Project Officer still has to act.</summary>
    Open,

    /// <summary>Complete.</summary>
    Done,

    /// <summary>
    /// Submitted to the HoD or the task assigner. The Project Officer's part is done, so it
    /// counts towards brief-ready and is shown as done.
    /// </summary>
    Submitted
}

public enum BriefingDeskRoute
{
    /// <summary>Answered with the update box on the card.</summary>
    Update,

    /// <summary>The Project Officer completes it directly.</summary>
    Direct,

    /// <summary>The Project Officer submits it; a HoD or the assigner approves.</summary>
    Approval
}

public sealed class BriefingDeskCheckVm
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>A short name for the card's "Next:" line ("Procurement"); the label when unset.</summary>
    public string? NextText { get; init; }

    public string Detail { get; init; } = string.Empty;

    public BriefingDeskCheckState State { get; init; }

    public BriefingDeskRoute Route { get; init; }

    public bool IsRequired { get; init; } = true;

    public string? ActionText { get; init; }

    public string? ActionUrl { get; init; }

    public string? StateText { get; init; }

    /// <summary>When set, the action opens this panel on the desk instead of navigating.</summary>
    public string? PanelId { get; init; }

    /// <summary>
    /// Replaces the line under the action button ("Needs HoD approval", "You complete it")
    /// when that would mislead, for example a step that only reopens the PO's own submission.
    /// </summary>
    public string? RouteHint { get; init; }

    public int Points { get; init; }

    /// <summary>
    /// Done, but close to falling due (a remark 7 to 9 days old): still counts towards ready for
    /// conference, shown in amber so the PO acts before it turns red.
    /// </summary>
    public bool IsDueSoon { get; init; }

    /// <summary>
    /// A second way out, opened in the project's own tab: "Wrong stage? Update it on the project
    /// page". <see cref="SecondaryTarget"/> is the tab name (ProjectTab.Name).
    /// </summary>
    public string? SecondaryText { get; init; }

    public string? SecondaryUrl { get; init; }

    public string? SecondaryTarget { get; init; }
}

/// <summary>The stage a remark confirms: stored on the remark as StageRef and StageNameSnapshot.</summary>
public sealed record BriefingDeskStageVm(string Code, string Label, string ProjectPageUrl, string ProjectTabName);

public sealed class BriefingDeskPlanPanelVm
{
    public string PanelId { get; init; } = string.Empty;

    public int ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string EditToken { get; init; } = string.Empty;

    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public DateOnly? PlannedStart { get; init; }

    public DateOnly? PlannedDue { get; init; }

    public string ReturnUrl { get; init; } = string.Empty;

    public string FullEditorUrl { get; init; } = string.Empty;

    /// <summary>Asked before the PDC form: the PDC belongs to this stage, so it must be the right one.</summary>
    public string StageLabel { get; init; } = string.Empty;

    public string ProjectPageUrl { get; init; } = string.Empty;

    public string ProjectTabName { get; init; } = string.Empty;
}

public sealed class BriefingDeskItemRenderVm
{
    public BriefingDeskItemVm Item { get; init; } = new();

    public bool IsOpen { get; init; }

    /// <summary>Text from a post that failed validation, restored into the box it came from.</summary>
    public string? Draft { get; init; }

    /// <summary>
    /// Which box <see cref="Draft"/> belongs to: <c>body</c> (the update box), <c>submit</c>
    /// (the closure note) or <c>blocked</c> (the blocker).
    /// </summary>
    public string DraftField { get; init; } = BriefingDeskDraftFields.Body;
}

public static class BriefingDeskDraftFields
{
    public const string Body = "body";
    public const string Submit = "submit";
    public const string Blocked = "blocked";
}

/// <summary>
/// Something sent for a decision. <paramref name="SentByName"/> is set when another user sent a
/// project-scoped submission (a plan or date correction), for example a previous PO.
/// </summary>
public sealed record BriefingDeskWaitingVm(
    string Title,
    string Detail,
    string WaitingOn,
    DateTimeOffset SinceUtc,
    string OpenUrl,
    string? SentByName = null);

public static class BriefingDeskLabels
{
    public static string KindKey(ConferenceItemKind kind) => kind switch
    {
        ConferenceItemKind.Project => "project",
        ConferenceItemKind.ProjectIdea => "idea",
        ConferenceItemKind.ActionTask => "task",
        _ => "item"
    };

    public static string KindLabel(ConferenceItemKind kind) => kind switch
    {
        ConferenceItemKind.Project => "Project",
        ConferenceItemKind.ProjectIdea => "Idea",
        ConferenceItemKind.ActionTask => "Task",
        _ => "Item"
    };
}

/// <summary>
/// The side column's own reminders and calendar, grouped (<c>BriefingDeskAgendaBuilder</c>).
/// Each group is capped; the "more" counts are what the Notebook and Calendar hold beyond it.
/// </summary>
public sealed class BriefingDeskAgendaVm
{
    public IReadOnlyList<BriefingDeskAgendaEntryVm> Overdue { get; init; } = Array.Empty<BriefingDeskAgendaEntryVm>();

    public int OverdueMoreCount { get; init; }

    public IReadOnlyList<BriefingDeskAgendaEntryVm> Coming { get; init; } = Array.Empty<BriefingDeskAgendaEntryVm>();

    /// <summary>Reminders in the window beyond the cap; listed in the Notebook's Reminders view.</summary>
    public int ComingReminderMoreCount { get; init; }

    /// <summary>Events in the window beyond the cap; listed in the Calendar.</summary>
    public int ComingEventMoreCount { get; init; }

    public IReadOnlyList<BriefingDeskAgendaEntryVm> Pinned { get; init; } = Array.Empty<BriefingDeskAgendaEntryVm>();

    public int PinnedMoreCount { get; init; }

    public bool IsEmpty => Overdue.Count == 0 && Coming.Count == 0 && Pinned.Count == 0;
}

public sealed record BriefingDeskAgendaEntryVm(string Title, string Meta, string Url, string Icon);
