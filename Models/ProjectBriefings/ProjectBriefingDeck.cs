using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectBriefings;

public enum ProjectBriefingLayout
{
    StandardBriefing = 1,
    ProjectUpdateSheet = 2
}

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

public enum ProjectBriefingNarrativeMode
{
    CapabilityOverview = 1,
    ProjectBrief = 2,
    Both = 3
}

public enum ProjectBriefingPresentationTheme
{
    EditorialLight = 1,
    GraphiteDark = 2
}

/// <summary>
/// Ceremonial closing message appended as the final slide of every generated deck.
/// </summary>
public enum ProjectBriefingClosingSlideType
{
    JaiHind = 1,
    ThankYou = 2
}

/// <summary>
/// Composition used by Standard PRISM Briefing project-brief slides.
/// Automatic selects one of the two concrete designs per project.
/// </summary>
public enum ProjectBriefingProjectBriefLayout
{
    Automatic = 1,
    Standard = 2,
    PhotoEmphasis = 3
}

public enum ProjectBriefingBrandingScope
{
    None = 0,
    CoverAndSummary = 1,
    AllSlides = 2
}

/// <summary>
/// Modular institutional-output blocks available on the optional SDD profile slide.
/// </summary>
public enum ProjectBriefingInstitutionalProfileModule
{
    ProjectsDeveloped = 1,
    Proliferation = 2,
    TrainingSupport = 3,
    IntellectualProperty = 4,
    Partnerships = 5
}

/// <summary>
/// Scope used by the institutional “Projects Developed” metric.
/// Original completed projects excludes records marked as rebuilds and is the authoritative default.
/// </summary>
public enum ProjectBriefingInstitutionalProjectScope
{
    OriginalCompleted = 1,
    AllCompletedIncludingRebuilds = 2
}

/// <summary>
/// Logical information rows available on each formal Project Update Sheet.
/// Project name is always rendered as the slide title and is therefore not a selectable table row.
/// </summary>
public enum ProjectBriefingUpdateSheetRow
{
    ProjectCost = 1,
    ArppPppNumber = 2,
    FundingAuthority = 3,
    AonDate = 4,
    SupplyOrder = 5,
    PdcOrCompletionStatus = 6,
    PresentStatus = 7,
    ProjectOfficer = 8,
    LineDirectorate = 9
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

    public ProjectBriefingLayout Layout { get; set; }
        = ProjectBriefingLayout.StandardBriefing;

    public ProjectBriefingPresentationMode PresentationMode { get; set; }
        = ProjectBriefingPresentationMode.Combined;

    public ProjectBriefingCostMode CostMode { get; set; }
        = ProjectBriefingCostMode.Both;

    public ProjectBriefingNarrativeMode NarrativeMode { get; set; }
        = ProjectBriefingNarrativeMode.CapabilityOverview;

    public ProjectBriefingPresentationTheme PresentationTheme { get; set; }
        = ProjectBriefingPresentationTheme.EditorialLight;

    public ProjectBriefingBrandingScope BrandingScope { get; set; }
        = ProjectBriefingBrandingScope.AllSlides;

    public bool IncludeCoverSlide { get; set; } = true;
    public bool IncludePortfolioSummarySlide { get; set; } = true;
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
