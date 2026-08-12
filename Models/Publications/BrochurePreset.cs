using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Publications;

/// <summary>
/// Shared institutional configuration for the Capability Brochure builder.
/// The preset stores publication choices only; project narrative, photo bytes,
/// approvals, preflight findings and generated-PDF verification remain live state.
/// </summary>
public sealed class BrochurePreset
{
    public long Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int SettingsSchemaVersion { get; set; } = 1;

    [Required, MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Subtitle { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Edition { get; set; } = string.Empty;

    [Required, MaxLength(180)]
    public string Strapline { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string CoverStyle { get; set; } = string.Empty;

    [Required, MaxLength(48)]
    public string InstitutionalCoverArtwork { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string NarrativeSource { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string PublicationProfile { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? IntroductionTitle { get; set; }

    [MaxLength(3000)]
    public string? IntroductionText { get; set; }

    [MaxLength(5000)]
    public string? PrintIntroText { get; set; }

    [MaxLength(3500)]
    public string? PrintFutureText { get; set; }

    [MaxLength(3500)]
    public string? PrintProcurementText { get; set; }

    [MaxLength(1200)]
    public string? PrintCentreStatement { get; set; }

    [MaxLength(1800)]
    public string? PrintDevelopingAgencyText { get; set; }

    [MaxLength(1200)]
    public string? PrintManufacturingAgencyText { get; set; }

    [MaxLength(4500)]
    public string? PrintVisionaryText { get; set; }

    [MaxLength(1800)]
    public string? PrintNewSimulatorsText { get; set; }

    [MaxLength(80)]
    public string? HandlingMarking { get; set; }

    public bool AllowTextOnlyProjects { get; set; }

    public bool IncludeBackCover { get; set; } = true;

    public int? CoverHeroProjectId { get; set; }

    public int? CoverHeroPhotoId { get; set; }

    public double CoverHeroFocalX { get; set; } = .5d;

    public double CoverHeroFocalY { get; set; } = .5d;

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

    public ICollection<BrochurePresetProject> Projects { get; set; }
        = new List<BrochurePresetProject>();
}

/// <summary>
/// Ordered project-level publication choices belonging to a shared brochure preset.
/// ProjectId is nullable so a hard-deleted project can be retained as a diagnostic
/// using ProjectNameSnapshot instead of silently corrupting the saved configuration.
/// Photo IDs are deliberately not foreign keys: if a project photograph is removed,
/// loading the preset can report the stale choice and safely fall back to Automatic.
/// </summary>
public sealed class BrochurePresetProject
{
    public long Id { get; set; }

    public long PresetId { get; set; }

    public BrochurePreset Preset { get; set; } = null!;

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required, MaxLength(160)]
    public string ProjectNameSnapshot { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public int? PrimaryPhotoId { get; set; }

    public int? SecondaryPhotoId { get; set; }

    public double PrimaryFocalX { get; set; } = .5d;

    public double PrimaryFocalY { get; set; } = .5d;

    public double SecondaryFocalX { get; set; } = .5d;

    public double SecondaryFocalY { get; set; } = .5d;

    [Required, MaxLength(32)]
    public string ImageMode { get; set; } = string.Empty;
}
