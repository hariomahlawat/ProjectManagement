namespace ProjectManagement.ViewModels;

public sealed class ProcurementEditVm
{
    public ProcurementEditInput Input { get; init; } = new();

    public bool CanEditIpaCost { get; init; }
    public bool CanEditAonCost { get; init; }
    public bool CanEditBenchmarkCost { get; init; }
    public bool CanEditL1Cost { get; init; }
    public bool CanEditPncCost { get; init; }
    public bool CanEditSupplyOrderDate { get; init; }
}
