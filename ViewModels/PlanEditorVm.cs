namespace ProjectManagement.ViewModels;

public static class PlanEditorModes
{
    public const string Exact = "Exact";
    public const string Durations = "Durations";
}

public sealed class PlanEditorVm
{
    public PlanEditVm Exact { get; init; } = new();
    public PlanDurationVm Durations { get; init; } = new();
    public string ActiveMode { get; init; } = PlanEditorModes.Exact;
}
