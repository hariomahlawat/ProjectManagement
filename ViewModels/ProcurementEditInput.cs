using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.ViewModels;

public sealed class ProcurementEditInput
{
    // The amounts are stored in decimal(18,2) columns; a larger value would fail the whole
    // save at the database, so it is rejected here with a validation message instead.
    // Extra decimal places are still accepted and rounded to paise by the column.
    public const string MaxAmountText = "9999999999999999.99";

    public int ProjectId { get; set; }

    [Range(typeof(decimal), "0", MaxAmountText, ParseLimitsInInvariantCulture = true)]
    public decimal? IpaCost { get; set; }

    [Range(typeof(decimal), "0", MaxAmountText, ParseLimitsInInvariantCulture = true)]
    public decimal? AonCost { get; set; }

    [Range(typeof(decimal), "0", MaxAmountText, ParseLimitsInInvariantCulture = true)]
    public decimal? BenchmarkCost { get; set; }

    [Range(typeof(decimal), "0", MaxAmountText, ParseLimitsInInvariantCulture = true)]
    public decimal? L1Cost { get; set; }

    [Range(typeof(decimal), "0", MaxAmountText, ParseLimitsInInvariantCulture = true)]
    public decimal? PncCost { get; set; }

    public DateOnly? SupplyOrderDate { get; set; }
}
