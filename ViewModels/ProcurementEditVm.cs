namespace ProjectManagement.ViewModels;

public sealed class ProcurementEditVm
{
    public ProcurementEditInput Input { get; init; } = new();

    public bool CanEditIpaCost { get; init; }
    public bool IsIpaManagedByArpp { get; init; }
    public string? IpaManagementMessage { get; init; }
    public bool CanEditAonCost { get; init; }
    public bool CanEditBenchmarkCost { get; init; }
    public bool CanEditL1Cost { get; init; }
    public bool CanEditPncCost { get; init; }
    public bool CanEditSupplyOrderDate { get; init; }

    /// <summary>Where the save returns; set by the briefing desk, null on Project Overview.</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>Prefix for element ids, so several editors can share one page.</summary>
    public string IdPrefix { get; init; } = string.Empty;
}
