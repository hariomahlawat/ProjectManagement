using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.ViewModels;

public sealed class ProcurementEditInput
{
    public int ProjectId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? IpaCost { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? AonCost { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? BenchmarkCost { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? L1Cost { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PncCost { get; set; }

    public DateOnly? SupplyOrderDate { get; set; }
}
