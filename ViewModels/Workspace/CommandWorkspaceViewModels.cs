namespace ProjectManagement.ViewModels.Workspace;

public sealed class CommandWorkspaceVm
{
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public string ActiveView { get; init; } = "portfolio";
    public int TotalOngoingProjects { get; init; }
    public IReadOnlyList<CommandFilterOptionVm> ParentCategoryOptions { get; init; } = Array.Empty<CommandFilterOptionVm>();
    public IReadOnlyList<int> SelectedParentCategoryIds { get; init; } = Array.Empty<int>();
    public string? ProjectSearch { get; init; }
    public bool PopulatedStagesOnly { get; init; }
    public IReadOnlyList<CommandStageSeriesPointVm> StageSeries { get; init; } = Array.Empty<CommandStageSeriesPointVm>();
    public IReadOnlyList<CommandStageColumnVm> StageColumns { get; init; } = Array.Empty<CommandStageColumnVm>();
    public IReadOnlyList<CommandOfficerWorkloadVm> Officers { get; init; } = Array.Empty<CommandOfficerWorkloadVm>();
    public IReadOnlyList<CommandFilterOptionVm> StageOptions { get; init; } = Array.Empty<CommandFilterOptionVm>();
    public int ProjectOfficerCount { get; init; }
    public CommandUsageSummaryVm UsageSummary { get; init; } = new();
}

public sealed class CommandUsageSummaryVm
{
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public DateTimeOffset TrackingInceptionUtc { get; init; }
    public int TrackingWorkingDays { get; init; }
    public int RequiredWorkingDays { get; init; } = 7;
    public bool ReviewAvailable { get; init; }
    public int TotalUsers { get; init; }
    public int ActiveToday { get; init; }
    public int SignedInUsers { get; init; }
    public int UsedErpUsers { get; init; }
    public int OperationalContributors { get; init; }
    public int AdoptionGap { get; init; }
    public int ReviewCaseCount { get; init; }
    public int RegularUsers { get; init; }
    public int NoUsageSevenWorkingDays { get; init; }
    public bool RegularClassificationAvailable { get; init; }
    public bool SevenDayReviewAvailable { get; init; }
    public IReadOnlyList<CommandAdoptionTrendPointVm> Trend { get; init; } =
        Array.Empty<CommandAdoptionTrendPointVm>();
    public IReadOnlyList<CommandAdoptionAttentionVm> Attention { get; init; } =
        Array.Empty<CommandAdoptionAttentionVm>();
}

public sealed record CommandAdoptionTrendPointVm(
    DateOnly Date,
    int SignedInUsers,
    int UsedErpUsers,
    int OperationalContributors,
    bool IsWorkingDay);

public sealed record CommandAdoptionAttentionVm(
    string UserId,
    string DisplayName,
    string Rank,
    string UserName,
    string Observation,
    DateTime? LastRecordedUseUtc,
    bool SignedInDuringPeriod);

public sealed record CommandFilterOptionVm(int Id, string Name);

public sealed record CommandStageSeriesPointVm(
    string StageCode,
    string StageName,
    string CategoryName,
    int Count);

public sealed class CommandStageColumnVm
{
    public string StageCode { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public int ProjectCount { get; init; }
    public IReadOnlyList<CommandStageCategoryVm> Categories { get; init; } = Array.Empty<CommandStageCategoryVm>();
}

public sealed class CommandStageCategoryVm
{
    public string CategoryName { get; init; } = string.Empty;
    public int ProjectCount { get; init; }
    public IReadOnlyList<CommandStageProjectVm> Projects { get; init; } = Array.Empty<CommandStageProjectVm>();
}

public sealed record CommandStageProjectVm(
    int ProjectId,
    string ProjectName,
    string OfficerName,
    string OpenUrl);

public sealed class CommandOfficerWorkloadVm
{
    public string UserId { get; init; } = string.Empty;
    public string OfficerName { get; init; } = string.Empty;
    public string Rank { get; init; } = string.Empty;
    public int ProjectCount { get; init; }
    public int IdeaCount { get; init; }
    public int OtherTaskCount { get; init; }
    public IReadOnlyList<CommandOfficerProjectVm> Projects { get; init; } = Array.Empty<CommandOfficerProjectVm>();
    public IReadOnlyList<CommandOfficerIdeaVm> Ideas { get; init; } = Array.Empty<CommandOfficerIdeaVm>();
    public IReadOnlyList<CommandOfficerTaskVm> OtherTasks { get; init; } = Array.Empty<CommandOfficerTaskVm>();
}

public sealed record CommandOfficerProjectVm(int ProjectId, string Name, string StageCode, string StageName, string OpenUrl);
public sealed record CommandOfficerIdeaVm(int IdeaId, string Title, string Status, string OpenUrl);
public sealed record CommandOfficerTaskVm(int TaskId, string Title, string Status, DateTime DueDate, string OpenUrl);

public sealed class OfficerWorkloadCardRenderVm
{
    public CommandOfficerWorkloadVm Officer { get; init; } = new();

    public bool CanReorder { get; init; }

    public bool ShowConferenceAction { get; init; }
}
