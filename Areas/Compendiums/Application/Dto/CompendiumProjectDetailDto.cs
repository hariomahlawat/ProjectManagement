namespace ProjectManagement.Areas.Compendiums.Application.Dto;

// SECTION: Compendium detail projection for web and PDF
public sealed class CompendiumProjectDetailDto
{
    public int ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CompletionYearText { get; init; } = "Not recorded";
    public string SponsoringLineDirectorateName { get; init; } = "Not recorded";
    public string ArmService { get; init; } = "Not recorded";
    public decimal? ProliferationCostLakhs { get; init; }
    public string Description { get; init; } = "Not recorded";
    public int? CoverPhotoId { get; init; }
    public int? CoverPhotoVersion { get; init; }
    public byte[]? CoverPhotoBytes { get; init; }
    public bool CoverPhotoAvailable { get; init; }
    public HistoricalExtrasDto? HistoricalExtras { get; init; }
}
