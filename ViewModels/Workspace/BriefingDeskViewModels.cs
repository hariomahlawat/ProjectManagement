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

            return open.Count == 1 ? open[0].Label : $"{open[0].Label} and {open.Count - 1} more";
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

public sealed record BriefingDeskFactVm(string Text, BriefingDeskTone Tone);

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

    /// <summary>Submitted and waiting on a HoD or the task assigner.</summary>
    Waiting
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

    public string Detail { get; init; } = string.Empty;

    public BriefingDeskCheckState State { get; init; }

    public BriefingDeskRoute Route { get; init; }

    public bool IsRequired { get; init; } = true;

    public string? ActionText { get; init; }

    public string? ActionUrl { get; init; }

    public string? StateText { get; init; }

    /// <summary>When set, the action opens this panel on the desk instead of navigating.</summary>
    public string? PanelId { get; init; }

    public int Points { get; init; }
}

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

    public bool IsOverdue { get; init; }

    public string ReturnUrl { get; init; } = string.Empty;

    public string FullEditorUrl { get; init; } = string.Empty;
}

public sealed class BriefingDeskItemRenderVm
{
    public BriefingDeskItemVm Item { get; init; } = new();

    public bool IsOpen { get; init; }

    /// <summary>Text from a post that failed validation, restored into the update box.</summary>
    public string? Draft { get; init; }
}

public sealed record BriefingDeskWaitingVm(
    string Title,
    string Detail,
    string WaitingOn,
    DateTimeOffset SinceUtc,
    string OpenUrl);

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
