using System.Collections.Generic;

namespace ProjectManagement.Services.Publications;

public enum BrochureNarrativeSource
{
    ProjectBrief = 1,
    CapabilityOverview = 2,
    FullDescription = 3
}

public enum BrochureCoverStyle
{
    Institutional = 1,
    Contemporary = 2
}

public enum BrochurePageLayoutKind
{
    FourCompact = 1,
    ThreeStandard = 2,
    TwoFeature = 3,
    SingleFeature = 4
}

public sealed record BrochureProjectListItemVm(
    int ProjectId,
    string ProjectName,
    string Lifecycle,
    string? ProjectCategory,
    string? TechnicalCategory,
    bool HasProjectBrief,
    bool HasCapabilityOverview,
    bool HasFullDescription,
    int ProjectBriefWordCount,
    int CapabilityOverviewWordCount,
    int FullDescriptionWordCount,
    bool HasPhoto,
    bool HasPrintReadyPhoto);

public sealed record BrochurePublicationProject(
    int ProjectId,
    string ProjectName,
    string? ProjectCategory,
    string? TechnicalCategory,
    string Narrative,
    int NarrativeWordCount,
    byte[]? Photo,
    bool PhotoIsLowResolution,
    string? PhotoSourceVariant);

public sealed record BrochureBuildOptions(
    string Title,
    string Subtitle,
    string Edition,
    string Strapline,
    BrochureCoverStyle CoverStyle,
    BrochureNarrativeSource NarrativeSource,
    string? IntroductionTitle,
    string? IntroductionText,
    string? HandlingMarking,
    string IssuerDisplayName,
    DateTimeOffset GeneratedAtUtc);

public sealed record BrochurePreflight(
    int SelectedProjectCount,
    int MissingNarrativeCount,
    int MissingPhotoCount,
    int LowResolutionPhotoCount,
    int LongNarrativeCount)
{
    public int TotalWarnings => MissingNarrativeCount + MissingPhotoCount + LowResolutionPhotoCount + LongNarrativeCount;
    public bool IsReady => SelectedProjectCount > 0 && TotalWarnings == 0;
}

public sealed record BrochurePublicationData(
    BrochureBuildOptions Options,
    IReadOnlyList<BrochurePublicationProject> Projects,
    BrochurePreflight Preflight);

public sealed record BrochureProjectFragment(
    BrochurePublicationProject Project,
    string Narrative,
    int NarrativeWordCount,
    bool IsContinuation,
    int FragmentNumber,
    int FragmentCount);

public sealed record BrochurePagePlan(
    BrochurePageLayoutKind Layout,
    IReadOnlyList<BrochureProjectFragment> Items);

public sealed record PublicationFontStatus(
    string PrimaryFamily,
    string DisplayFamily,
    bool DmSansAvailable,
    bool AlatsiAvailable,
    IReadOnlyList<string> MissingDmSansFiles);
