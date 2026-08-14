using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Publications;

/// <summary>
/// Shared, reusable Compendium publication configuration. This entity stores presentation
/// choices only. Project facts remain authoritative in the Project aggregate and are re-read
/// every time a Compendium is loaded, reviewed, previewed or generated.
/// </summary>
public sealed class CompendiumPreset
{
    public long Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int SettingsSchemaVersion { get; set; } = 5;

    [Required, MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Subtitle { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Edition { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? HandlingMarking { get; set; }

    [Required, MaxLength(32)]
    public string NarrativeSource { get; set; } = "ProjectBrief";

    [Required, MaxLength(32)]
    public string GroupingMode { get; set; } = "TechnicalCategory";

    [Required, MaxLength(32)]
    public string SortMode { get; set; } = "Manual";

    [Required, MaxLength(32)]
    public string CoverImageMode { get; set; } = "Automatic";

    public int? CoverHeroProjectId { get; set; }
    public int? CoverHeroPhotoId { get; set; }
    public double CoverFocalX { get; set; } = .5d;
    public double CoverFocalY { get; set; } = .5d;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;

    [Required, MaxLength(450)]
    public string LastModifiedByUserId { get; set; } = string.Empty;
    public ApplicationUser LastModifiedByUser { get; set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<CompendiumPresetSection> Sections { get; set; } = new List<CompendiumPresetSection>();
    public ICollection<CompendiumPresetProject> Projects { get; set; } = new List<CompendiumPresetProject>();
}

/// <summary>
/// A first-class, publication-only custom section. The stable key is intentionally independent
/// from the database identity so unsaved browser sections can survive save/load without being
/// coupled to database-generated IDs.
/// </summary>
public sealed class CompendiumPresetSection
{
    public long Id { get; set; }
    public long PresetId { get; set; }
    public CompendiumPreset Preset { get; set; } = null!;

    [Required, MaxLength(40)]
    public string SectionKey { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string NormalizedName { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public ICollection<CompendiumPresetProject> Projects { get; set; } = new List<CompendiumPresetProject>();
}

/// <summary>
/// Ordered publication membership for a saved Compendium. ProjectId is nullable so deletion of
/// an authoritative project can be reported on load instead of silently corrupting the preset.
/// </summary>
public sealed class CompendiumPresetProject
{
    public long Id { get; set; }
    public long PresetId { get; set; }
    public CompendiumPreset Preset { get; set; } = null!;

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required, MaxLength(160)]
    public string ProjectNameSnapshot { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public int? PrimaryPhotoId { get; set; }

    public double PrimaryFocalX { get; set; } = .5d;
    public double PrimaryFocalY { get; set; } = .5d;

    [Required, MaxLength(32)]
    public string ImageSelectionMode { get; set; } = "Automatic";

    /// <summary>
    /// Optional per-project publication narrative override. Null means inherit the Compendium default.
    /// </summary>
    [MaxLength(32)]
    public string? NarrativeSourceOverride { get; set; }

    public long? CustomSectionId { get; set; }
    public CompendiumPresetSection? CustomSection { get; set; }

    /// <summary>
    /// Legacy phase-25 compatibility snapshot. Phase 26 uses CustomSectionId/CompendiumPresetSection;
    /// keeping this column allows safe migration and rollback and makes older saved rows intelligible.
    /// </summary>
    [MaxLength(120)]
    public string? CustomSectionName { get; set; }
}
