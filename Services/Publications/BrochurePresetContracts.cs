namespace ProjectManagement.Services.Publications;

public enum BrochurePresetDiagnosticSeverity
{
    Information = 1,
    Warning = 2
}

public sealed record BrochurePresetDiagnostic(
    BrochurePresetDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? ProjectId = null,
    string? ProjectName = null);

public sealed record BrochurePresetSummaryVm(
    long Id,
    string Name,
    string? Description,
    BrochurePublicationProfile PublicationProfile,
    int ProjectCount,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedByDisplay,
    string RowVersion);

public sealed record BrochurePresetProjectConfiguration(
    int ProjectId,
    int? PrimaryPhotoId,
    int? SecondaryPhotoId,
    double PrimaryFocalX,
    double PrimaryFocalY,
    double SecondaryFocalX,
    double SecondaryFocalY,
    BrochureImageMode ImageMode);

/// <summary>
/// Durable builder configuration. Approval fingerprints, preflight results and PDF
/// verification are intentionally absent: a saved brochure never grants current approval.
/// </summary>
public sealed record BrochurePresetConfiguration(
    string Title,
    string Subtitle,
    string Edition,
    string Strapline,
    BrochureCoverStyle CoverStyle,
    BrochureInstitutionalCoverArtwork InstitutionalCoverArtwork,
    BrochureNarrativeSource NarrativeSource,
    BrochurePublicationProfile PublicationProfile,
    string? IntroductionTitle,
    string? IntroductionText,
    string? PrintIntroText,
    string? PrintFutureText,
    string? PrintProcurementText,
    string? PrintCentreStatement,
    string? PrintDevelopingAgencyText,
    string? PrintManufacturingAgencyText,
    string? PrintVisionaryText,
    string? PrintNewSimulatorsText,
    string? HandlingMarking,
    bool AllowTextOnlyProjects,
    bool IncludeBackCover,
    int? CoverHeroProjectId,
    int? CoverHeroPhotoId,
    double CoverHeroFocalX,
    double CoverHeroFocalY,
    IReadOnlyList<BrochurePresetProjectConfiguration> Projects,
    string? FrontCoverKicker = null,
    string? FrontCoverDescriptor = null,
    bool ShowFrontCoverTitle = true,
    bool ShowFrontCoverSubtitle = true,
    bool ShowFrontCoverEdition = true,
    bool ShowFrontCoverStrapline = true,
    string? BackCoverKicker = null,
    string? BackCoverStrapline = null,
    string? BackCoverEdition = null);

public sealed record BrochurePresetLoadResult(
    BrochurePresetSummaryVm Preset,
    BrochurePresetConfiguration Configuration,
    IReadOnlyList<BrochurePresetDiagnostic> Diagnostics);

public sealed record BrochurePresetMutationResult(
    BrochurePresetSummaryVm Preset);

public sealed class BrochurePresetConcurrencyException : Exception
{
    public BrochurePresetConcurrencyException(string message) : base(message) { }
}
