namespace ProjectManagement.Areas.Compendiums.Application.Dto;

// SECTION: Historical-only project facts
public sealed class HistoricalExtrasDto
{
    public decimal? RdCostLakhs { get; init; }
    public string TotStatusText { get; init; } = "Not recorded";
    public string TotCompletedOnText { get; init; } = "Not recorded";
    public int ProliferationTotalAllTime { get; init; }
    public int ProliferationSddAllTime { get; init; }
    public int ProliferationAbw515AllTime { get; init; }
}
