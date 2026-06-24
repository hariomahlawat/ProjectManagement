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
    public string? OfficerSearch { get; init; }
    public string? OfficerStageCode { get; init; }
    public string OfficerWorkType { get; init; } = "all";
    public int ProjectOfficerCount { get; init; }
}

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
