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

public enum BrochureImageMode
{
    Automatic = 1,
    Single = 2,
    GalleryTwo = 3
}

public enum PublicationIssueSeverity
{
    Blocker = 1,
    Warning = 2,
    Information = 3
}

public enum BrochurePhotoQuality
{
    Low = 1,
    Acceptable = 2,
    PrintReady = 3,
    Excellent = 4
}

public enum BrochurePhotoPreviewKind
{
    Thumbnail = 1,
    Source = 2
}

public enum BrochurePreflightIssueCode
{
    ProjectUnavailable = 1,
    MissingNarrative = 2,
    MissingPrimaryPhoto = 3,
    SelectedPhotoInvalid = 4,
    SelectedPhotoUnavailable = 5,
    LowResolutionPhoto = 6,
    GallerySecondPhotoRequired = 7,
    GallerySecondPhotoInvalid = 8,
    GallerySecondPhotoUnavailable = 9,
    LongNarrative = 10,
    TextOnlyProject = 11,
    SelectionLimitExceeded = 12
}

public sealed record BrochurePhotoOptionVm(
    int PhotoId,
    int Version,
    string? Caption,
    int Width,
    int Height,
    bool IsCover,
    bool IsLowResolution);

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
    int? DefaultPrimaryPhotoId,
    int? DefaultSecondaryPhotoId,
    IReadOnlyList<BrochurePhotoOptionVm> Photos)
{
    public bool HasPhoto => Photos.Count > 0;
    public bool HasPrintReadyPhoto => Photos.Any(photo =>
        !photo.IsLowResolution
        && photo.Width >= 1600
        && photo.Height >= 900);
}

public sealed record BrochureProjectSelection(
    int ProjectId,
    int? PrimaryPhotoId,
    int? SecondaryPhotoId,
    double PrimaryFocalX,
    double PrimaryFocalY,
    double SecondaryFocalX,
    double SecondaryFocalY,
    BrochureImageMode ImageMode);

public sealed record BrochurePhotoReference(int ProjectId, int PhotoId);

public sealed record BrochurePhotoProbe(
    int ProjectId,
    int PhotoId,
    bool IsReady,
    bool IsPrintReady,
    int Width,
    int Height,
    string? SourceVariant,
    string? FailureReason = null,
    BrochurePhotoQuality Quality = BrochurePhotoQuality.Low);

public sealed record BrochurePhotoPreview(
    byte[] Content,
    string ContentType,
    int SourceWidth,
    int SourceHeight,
    string SourceVariant,
    BrochurePhotoQuality Quality);

public sealed record BrochurePhotoRenderRequest(
    int ProjectId,
    int PhotoId,
    double FocalX,
    double FocalY,
    int TargetWidth = 1920,
    int TargetHeight = 1080);

public sealed record BrochurePublicationImage(
    int PhotoId,
    byte[] Content,
    int SourceWidth,
    int SourceHeight,
    bool IsPrintReady,
    string SourceVariant,
    BrochurePhotoQuality Quality = BrochurePhotoQuality.Low);

public sealed record BrochurePublicationProject(
    int ProjectId,
    string ProjectName,
    string? ProjectCategory,
    string? TechnicalCategory,
    string Narrative,
    int NarrativeWordCount,
    BrochurePublicationImage? PrimaryPhoto,
    BrochurePublicationImage? SecondaryPhoto,
    BrochureImageMode ImageMode);

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
    bool AllowTextOnlyProjects,
    DateTimeOffset GeneratedAtUtc);

public sealed record BrochurePreflightIssue(
    BrochurePreflightIssueCode Code,
    PublicationIssueSeverity Severity,
    int? ProjectId,
    string? ProjectName,
    string Message);

public sealed record BrochurePreflight(
    int SelectedProjectCount,
    IReadOnlyList<BrochurePreflightIssue> Issues)
{
    public int BlockerCount => Issues.Count(issue => issue.Severity == PublicationIssueSeverity.Blocker);
    public int WarningCount => Issues.Count(issue => issue.Severity == PublicationIssueSeverity.Warning);
    public int InformationCount => Issues.Count(issue => issue.Severity == PublicationIssueSeverity.Information);
    public bool CanGenerate => SelectedProjectCount > 0 && BlockerCount == 0;
    public bool IsPublicationReady => CanGenerate && WarningCount == 0;
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
    IReadOnlyList<string> MissingDmSansFiles,
    string SourceDescription);
