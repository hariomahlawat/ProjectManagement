using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectBriefings;

public enum ProjectBriefingPresentationMode
{
    ExecutiveTable = 1,
    DetailedProjects = 2,
    Combined = 3
}

public enum ProjectBriefingCostMode
{
    CostRdOnly = 1,
    ProliferationOnly = 2,
    Both = 3,
    None = 4
}

public enum ProjectBriefingPresentationTheme
{
    EditorialLight = 1,
    GraphiteDark = 2
}

public enum ProjectBriefingBrandingScope
{
    None = 0,
    CoverAndSummary = 1,
    AllSlides = 2
}

public sealed class ProjectBriefingDeck
{
    public long Id { get; set; }

    // The creator is retained for attribution and audit. Deck visibility is command-workspace wide.
    [Required, MaxLength(450)]
    public string OwnerUserId { get; set; } = string.Empty;
    public ApplicationUser OwnerUser { get; set; } = null!;

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }
    public ApplicationUser? LastModifiedByUser { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(600)]
    public string? Description { get; set; }

    public ProjectBriefingPresentationMode PresentationMode { get; set; }
        = ProjectBriefingPresentationMode.Combined;

    public ProjectBriefingCostMode CostMode { get; set; }
        = ProjectBriefingCostMode.Both;

    public ProjectBriefingPresentationTheme PresentationTheme { get; set; }
        = ProjectBriefingPresentationTheme.EditorialLight;

    public ProjectBriefingBrandingScope BrandingScope { get; set; }
        = ProjectBriefingBrandingScope.AllSlides;

    public bool IncludeStageSummary { get; set; } = true;
    public bool IncludeProjectCategorySummary { get; set; }
    public bool IncludeTechnicalCategorySummary { get; set; }

    [MaxLength(80)]
    public string? HandlingMarking { get; set; }

    public string? SelectionRulesJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? LastGeneratedAtUtc { get; set; }

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ProjectBriefingDeckItem> Items { get; set; }
        = new List<ProjectBriefingDeckItem>();
}

public sealed class ProjectBriefingDeckItem
{
    public long Id { get; set; }

    public long DeckId { get; set; }
    public ProjectBriefingDeck Deck { get; set; } = null!;

    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int SortOrder { get; set; }

    [MaxLength(1200)]
    public string? BriefDescriptionOverride { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
}
