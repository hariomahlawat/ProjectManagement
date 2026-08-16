using ProjectManagement.Services.Compendiums;

namespace ProjectManagement.Services.Publications;

public enum CompendiumPresetDiagnosticSeverity
{
    Information = 0,
    Warning = 1
}

public sealed record CompendiumPresetDiagnostic(
    CompendiumPresetDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? ProjectId = null,
    string? ProjectName = null);

public sealed record CompendiumPresetSummaryVm(
    long Id,
    string Name,
    string? Description,
    int ProjectCount,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedByDisplay,
    string RowVersion);

/// <summary>
/// Stable, publication-only section definition. SectionKey is generated in the authoring client
/// and persisted unchanged so empty sections and section order survive save/load independently
/// of project membership.
/// </summary>
public sealed record CompendiumPresetSectionConfiguration(
    string SectionKey,
    string Name,
    int SortOrder);

public sealed record CompendiumPresetProjectConfiguration(
    int ProjectId,
    int? PrimaryPhotoId = null,
    double PrimaryFocalX = .5d,
    double PrimaryFocalY = .5d,
    CompendiumImageSelectionMode ImageSelectionMode = CompendiumImageSelectionMode.Automatic)
{
    public string? CustomSectionKey { get; init; }
    public string? CustomSectionName { get; init; }
    public CompendiumNarrativeSource? NarrativeSourceOverride { get; init; }
    public CompendiumImageFitMode ImageFitMode { get; init; } = CompendiumImageFitMode.Fill;
    public CompendiumDossierLayout DossierLayout { get; init; } = CompendiumDossierLayout.Automatic;
    public CompendiumBalancedTextFlowMode BalancedTextFlowMode { get; init; } = CompendiumBalancedTextFlowMode.FlowBelowImage;
    public CompendiumNarrativeAlignment? NarrativeAlignmentOverride { get; init; }
    public string? AdditionalNote { get; init; }
    public int DossierImageCount { get; init; } = 1;
    public int? SupportingPhoto1Id { get; init; }
    public double SupportingPhoto1FocalX { get; init; } = .5d;
    public double SupportingPhoto1FocalY { get; init; } = .5d;
    public CompendiumImageFitMode SupportingPhoto1FitMode { get; init; } = CompendiumImageFitMode.Fill;
    public int? SupportingPhoto2Id { get; init; }
    public double SupportingPhoto2FocalX { get; init; } = .5d;
    public double SupportingPhoto2FocalY { get; init; } = .5d;
    public CompendiumImageFitMode SupportingPhoto2FitMode { get; init; } = CompendiumImageFitMode.Fill;
}

public sealed record CompendiumPresetCoverImageConfiguration(
    CompendiumCoverSurface Surface,
    string SlotKey,
    CompendiumCoverImageMode ImageMode,
    int? ProjectId,
    int? PhotoId,
    double FocalX,
    double FocalY,
    CompendiumImageFitMode FitMode,
    int SortOrder);

public sealed record CompendiumPresetPhotoPreferenceConfiguration(
    int ProjectId,
    int PhotoId,
    bool PreferredForPublication,
    bool SuitableForCoverHero);

public sealed record CompendiumCoverConfiguration(
    CompendiumCoverImageMode ImageMode = CompendiumCoverImageMode.Automatic,
    int? HeroProjectId = null,
    int? HeroPhotoId = null,
    double FocalX = .5d,
    double FocalY = .5d);

public sealed record CompendiumCoverDesignConfiguration
{
    public CompendiumFrontCoverTemplate FrontTemplate { get; init; } = CompendiumFrontCoverTemplate.InstitutionalHero;
    public CompendiumBackCoverTemplate BackTemplate { get; init; } = CompendiumBackCoverTemplate.MinimalInstitutional;
    public CompendiumPublicationTheme PublicationTheme { get; init; } = CompendiumPublicationTheme.InstitutionalGreen;
    public CompendiumCoverBackgroundTreatment BackgroundTreatment { get; init; } = CompendiumCoverBackgroundTreatment.Solid;
    public string? FrontTitle { get; init; }
    public string? FrontSubtitle { get; init; }
    public string? FrontEdition { get; init; }
    public string? FrontEyebrow { get; init; }
    public string? BackTitle { get; init; }
    public string? BackSubtitle { get; init; }
    public string? BackEdition { get; init; }
    public string? BackEyebrow { get; init; }
    public bool ShowFrontTitle { get; init; } = true;
    public bool ShowFrontSubtitle { get; init; } = true;
    public bool ShowFrontEdition { get; init; } = true;
    public bool ShowFrontLeftLogo { get; init; } = true;
    public bool ShowFrontRightLogo { get; init; } = true;
    public CompendiumCoverLogoPlacement FrontLogoPlacement { get; init; } = CompendiumCoverLogoPlacement.TopCorners;
    public bool ShowBackTitle { get; init; } = true;
    public bool ShowBackSubtitle { get; init; } = true;
    public bool ShowBackEdition { get; init; } = true;
    public bool ShowBackLeftLogo { get; init; } = true;
    public bool ShowBackRightLogo { get; init; } = true;
    public CompendiumCoverLogoPlacement BackLogoPlacement { get; init; } = CompendiumCoverLogoPlacement.TopCorners;
    public IReadOnlyList<CompendiumPresetCoverImageConfiguration> Images { get; init; }
        = Array.Empty<CompendiumPresetCoverImageConfiguration>();
}

public sealed record CompendiumPresetConfiguration(
    string Title,
    string Subtitle,
    string Edition,
    string? HandlingMarking,
    IReadOnlyList<CompendiumPresetProjectConfiguration> Projects)
{
    public CompendiumCoverConfiguration Cover { get; init; } = new();
    public CompendiumCoverDesignConfiguration CoverDesign { get; init; } = new();
    public IReadOnlyList<CompendiumPresetPhotoPreferenceConfiguration> PhotoPreferences { get; init; }
        = Array.Empty<CompendiumPresetPhotoPreferenceConfiguration>();
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectBrief;
    public CompendiumNarrativeAlignment DefaultNarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public CompendiumGroupingMode GroupingMode { get; init; } = CompendiumGroupingMode.TechnicalCategory;
    public CompendiumSortMode SortMode { get; init; } = CompendiumSortMode.Manual;
    public IReadOnlyList<CompendiumPresetSectionConfiguration> Sections { get; init; }
        = Array.Empty<CompendiumPresetSectionConfiguration>();

    public CompendiumPresetConfiguration(
        string title,
        string subtitle,
        string edition,
        string? handlingMarking,
        IReadOnlyList<int> projectIds)
        : this(
            title,
            subtitle,
            edition,
            handlingMarking,
            projectIds
                .Where(projectId => projectId > 0)
                .Select(projectId => new CompendiumPresetProjectConfiguration(projectId))
                .ToArray())
    {
    }

    public IReadOnlyList<int> ProjectIds => Projects.Select(project => project.ProjectId).ToArray();
}

public sealed record CompendiumPresetLoadResult(
    CompendiumPresetSummaryVm Preset,
    CompendiumPresetConfiguration Configuration,
    IReadOnlyList<CompendiumPresetDiagnostic> Diagnostics);

public sealed record CompendiumPresetMutationResult(CompendiumPresetSummaryVm Preset);

public sealed class CompendiumPresetConcurrencyException : Exception
{
    public CompendiumPresetConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
