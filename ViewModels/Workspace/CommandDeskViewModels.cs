namespace ProjectManagement.ViewModels.Workspace;

/// <summary>
/// The Command desk: one read-only page for the HoD and the Comdt that opens with what moved
/// forward, then what waits on the viewer, what is lagging and which registers have gone quiet.
/// </summary>
public sealed class CommandDeskVm
{
    public DateOnly Today { get; init; }
    public int WindowDays { get; init; } = 30;

    // SECTION: What moved forward
    public IReadOnlyList<CommandDeskCountVm> MovedForward { get; init; } = Array.Empty<CommandDeskCountVm>();
    public IReadOnlyList<CommandDeskWinVm> Wins { get; init; } = Array.Empty<CommandDeskWinVm>();

    // SECTION: Waiting on you
    public IReadOnlyList<CommandDeskDecisionVm> WaitingOnYou { get; init; } = Array.Empty<CommandDeskDecisionVm>();
    public int WaitingOnYouTotal { get; init; }
    public bool CanOpenDecisionCentre { get; init; }

    // SECTION: What's lagging
    public IReadOnlyList<CommandDeskPdcVm> PdcWatch { get; init; } = Array.Empty<CommandDeskPdcVm>();
    public int PdcOverdueCount { get; init; }
    public int PdcDueSoonCount { get; init; }
    public IReadOnlyList<CommandDeskDirectionVm> Directions { get; init; } = Array.Empty<CommandDeskDirectionVm>();
    public int DirectionsOlderThanWeekCount { get; init; }
    public IReadOnlyList<CommandDeskTaskVm> Tasks { get; init; } = Array.Empty<CommandDeskTaskVm>();
    public int TasksBlockedCount { get; init; }
    public int TasksOverdueCount { get; init; }
    public int TasksDueSoonCount { get; init; }
    public IReadOnlyList<CommandDeskAttentionVm> Attention { get; init; } = Array.Empty<CommandDeskAttentionVm>();
    public bool NothingReturned { get; init; }

    // SECTION: What's gone quiet
    public IReadOnlyList<CommandDeskRegisterVm> QuietRegisters { get; init; } = Array.Empty<CommandDeskRegisterVm>();
    public int CurrentRegisterCount { get; init; }

    public bool IsLaggingEmpty => PdcWatch.Count == 0 && Directions.Count == 0 && Tasks.Count == 0 && Attention.Count == 0;
}

public sealed record CommandDeskCountVm(int Count, string Label, string? Url);

public sealed record CommandDeskWinVm(string Text, string Meta, DateOnly On, string? Url);

public sealed record CommandDeskDecisionVm(string Title, string TypeLabel, string ByName, string AgeLabel, string ActionLabel, string Url);

public sealed record CommandDeskPdcVm(
    int ProjectId,
    string ProjectName,
    string OfficerName,
    string StageName,
    DateOnly Pdc,
    int DaysFromToday,
    string WhenLabel,
    bool IsOverdue,
    string? Note,
    string Url);

public sealed record CommandDeskDirectionVm(
    string Title,
    string KindLabel,
    string OfficerName,
    string DirectionText,
    string IssuedBy,
    DateOnly IssuedOn,
    int AgeDays,
    string AgeLabel,
    bool IsOlderThanWeek,
    string Url);

public sealed record CommandDeskTaskVm(
    string Title,
    string Who,
    DateOnly Due,
    string StateLabel,
    bool IsDanger,
    string? BlockedReason,
    string Url);

public sealed record CommandDeskAttentionVm(
    string Key,
    int Count,
    string Label,
    IReadOnlyList<CommandDeskOfficerCountVm> ByOfficer);

public sealed record CommandDeskOfficerCountVm(string OfficerName, int Count, string? Url);

public sealed record CommandDeskRegisterVm(
    string Label,
    DateOnly? LastOn,
    int? DaysSince,
    int ExpectedDays,
    string Url);
