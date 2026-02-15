namespace ProjectManagement.Areas.Compendiums.Application.Dto;

// SECTION: Compendium list card projection
public sealed class CompendiumProjectCardDto
{
    public int ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CompletionYearText { get; init; } = "Not recorded";
    public string SponsoringLineDirectorateName { get; init; } = "Not recorded";
    public string ArmService { get; init; } = "Not recorded";
    public decimal? ProliferationCostLakhs { get; init; }
    public bool HasCoverPhoto { get; init; }
    public int? CoverPhotoId { get; init; }
    public int? CoverPhotoVersion { get; init; }
}
