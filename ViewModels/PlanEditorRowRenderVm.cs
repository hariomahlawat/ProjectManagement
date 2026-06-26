namespace ProjectManagement.ViewModels;

public sealed class PlanExactRowRenderVm
{
    public PlanEditVm.PlanEditRow Row { get; init; } = new();
    public int Index { get; init; }
    public bool IsDisabled { get; init; }
}

public sealed class PlanDurationRowRenderVm
{
    public PlanDurationRowVm Row { get; init; } = new();
    public int Index { get; init; }
    public bool IsDisabled { get; init; }
}
