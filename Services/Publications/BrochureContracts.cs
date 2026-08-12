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

/// <summary>
/// Curated offline artwork shipped with PRISM for the institutional Cover A hero.
/// The reference artwork remains the default; the remaining options are professionally
/// art-directed alternatives that preserve the same SDD technology narrative.
/// </summary>
public enum BrochureInstitutionalCoverArtwork
{
    ReferenceOriginal = 1,
    PremiumGreenGold = 2,
    CinematicCyber = 3,
    ExecutiveTeal = 4,
    LuminousHalo = 5
}

public enum BrochurePublicationProfile
{
    PrintCompact = 1,
    DigitalComfortable = 2
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

public enum BrochurePrintLayoutVariant
{
    // Compact remains an emergency 8.5 pt layout for a single pathological project only.
    Compact = 1,
    Balanced = 2,
    Visual = 3,
    Dense = 4
}

public enum BrochureInstitutionalArtworkIdentityMode
{
    FullArtwork = 1,
    BackgroundOnly = 2
}

public enum BrochureFloatSplitKind
{
    None = 0,
    Paragraph = 1,
    Sentence = 2,
    Word = 3
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
    SelectionLimitExceeded = 12,
    UnconfirmedPrimaryPhoto = 13,
    CoverHeroInvalid = 14,
    CoverHeroUnavailable = 15,
    PrintNarrativeTooLong = 16,
    PrintInstitutionalContentMissing = 17,
    PrintInstitutionalContentTooLong = 18,
    PrintClosingPageStandalone = 19,
    PrintFrontPageDoesNotFit = 20,
    PrintFrontPageTightFit = 21,
    PrintPageUnderUtilized = 22,
    PrintSmartFlowAvailable = 23,
    ProjectReviewRequired = 24,
    ProjectReviewStale = 25,
    CoverReviewRequired = 26,
    CoverReviewStale = 27
}


public sealed record BrochurePrintMatter(
    string? CentreStatement,
    string? OpeningNarrative,
    string? FutureNarrative,
    string? ProcurementGuidance,
    string? DevelopingAgency,
    string? ManufacturingAgency,
    string? VisionaryHorizons,
    string? NewSimulatorsGuidance);

public sealed record BrochurePrintPlanningItem(
    int ProjectId,
    string ProjectName,
    string Narrative,
    BrochureImageMode ImageMode,
    bool HasPrimaryPhoto,
    bool HasSecondaryPhoto)
{
    public int NarrativeWordCount => BrochureLayoutPlanner.CountWords(Narrative);

    // Compatibility constructor retained for older tests/helpers that only supplied a word count.
    public BrochurePrintPlanningItem(
        int projectId,
        string projectName,
        int narrativeWordCount,
        BrochureImageMode imageMode,
        bool hasPrimaryPhoto,
        bool hasSecondaryPhoto)
        : this(
            projectId,
            projectName,
            narrativeWordCount <= 0
                ? string.Empty
                : string.Join(" ", Enumerable.Repeat("word", narrativeWordCount)),
            imageMode,
            hasPrimaryPhoto,
            hasSecondaryPhoto)
    {
    }
}

public sealed record BrochurePrintProjectMeasurement(
    int ProjectId,
    BrochurePrintLayoutVariant Variant,
    float TotalHeightPoints,
    float TitleHeightPoints,
    float TitleFontSize,
    float BodyFontSize,
    float BodyLineHeight,
    float ImageWidthPoints,
    float BodyPaddingPoints,
    float TextWidthPoints,
    float TextHeightPoints,
    float ImageHeightPoints,
    int QualityRank,
    string LeadingNarrative = "",
    string TrailingNarrative = "",
    float LeadingTextHeightPoints = 0f,
    float TrailingTextHeightPoints = 0f,
    float FullTextWidthPoints = 0f,
    float PrimaryImageHeightPoints = 0f,
    float SecondaryImageHeightPoints = 0f,
    bool UsesFloatLayout = false,
    string ContinuationNarrative = "",
    float ContinuationTextHeightPoints = 0f,
    BrochureFloatSplitKind FloatSplitKind = BrochureFloatSplitKind.None,
    float RemainderGapPoints = 0f,
    float ParagraphSpacingPoints = 0f,
    bool UsesSecondaryImage = false,
    float VisualQualityScore = 0f,
    float TitleMinimumHeightPoints = 18f);

public sealed record BrochurePrintPlannedProject(
    int ProjectIndex,
    BrochurePrintProjectMeasurement Measurement);

public sealed record BrochurePrintClosingMeasurement(
    float TotalHeightPoints,
    float VisionPanelHeightPoints,
    float NewSimulatorsHeightPoints,
    float StraplineHeightPoints);

public sealed record BrochurePrintFrontPagePlan(
    bool Fits,
    float HeroHeightPoints,
    float CentreBlockHeightPoints,
    float CentreFontSize,
    float BodyBlockHeightPoints,
    float BodyFontSize,
    float BodyLineHeight,
    float BodySpacingPoints,
    float ContactBlockHeightPoints,
    float ContactFontSize,
    float StraplineHeightPoints,
    float TotalUsedHeightPoints,
    int UtilizationPercent,
    bool UsesMinimumTypography,
    BrochureCoverStyle CoverStyle);

public sealed record BrochurePrintCompactPage(
    IReadOnlyList<BrochurePrintPlannedProject> Projects,
    float MeasuredProjectHeightPoints,
    float MeasuredPhysicalUsedPoints,
    float CapacityPoints,
    bool IncludesClosingMatter,
    float ClosingHeightPoints,
    int UtilizationPercent,
    float ExtraModuleVerticalPaddingPoints = 0f,
    float ExtraInterModuleSpacingPoints = 0f)
{
    public IReadOnlyList<int> ProjectIndexes => Projects.Select(project => project.ProjectIndex).ToArray();
}

public sealed record BrochurePrintSheetSummary(
    int SheetNumber,
    string Kind,
    int? FirstProjectOrdinal,
    int? LastProjectOrdinal,
    int ProjectCount,
    bool IncludesClosingMatter,
    int UtilizationPercent,
    string Label,
    bool IsFinal = false);

public sealed record BrochurePrintOrderMove(
    int ProjectId,
    string ProjectName,
    int FromOrdinal,
    int ToOrdinal);

public sealed record BrochurePrintFlowSuggestion(
    IReadOnlyList<int> SuggestedProjectIds,
    int CurrentPageCount,
    int SuggestedPageCount,
    int CurrentLowestProjectUtilizationPercent,
    int SuggestedLowestProjectUtilizationPercent,
    int CurrentAverageUtilizationPercent,
    int SuggestedAverageUtilizationPercent,
    int MovedProjectCount,
    int TotalPositionShift,
    int DenseProjectCount,
    int AutomaticSingleProjectCount,
    int MinimumImageWidthPoints,
    string AdaptiveTreatmentSummary,
    IReadOnlyList<BrochurePrintOrderMove> Moves,
    IReadOnlyList<BrochurePrintSheetSummary> SuggestedSheetPlan,
    string Summary);

public sealed record BrochurePrintCompactPlan(
    IReadOnlyList<BrochurePrintCompactPage> Pages,
    BrochurePrintFrontPagePlan FrontPage,
    BrochurePrintClosingMeasurement ClosingMatter,
    int EstimatedTotalPageCount,
    int AverageContentUtilizationPercent,
    bool ClosingMatterSharesFinalPage,
    int ClosingPageProjectCount,
    int? LowestProjectPageUtilizationPercent,
    int? FinalPageUtilizationPercent,
    IReadOnlyList<BrochurePrintSheetSummary> SheetPlan,
    BrochurePrintFlowSuggestion? SmartFlowSuggestion = null);

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

public sealed record BrochureProjectReviewVm(
    int ProjectId,
    string ProjectName,
    string Lifecycle,
    string? ProjectCategory,
    string? TechnicalCategory,
    string Narrative,
    bool HasNarrative,
    int NarrativeWordCount,
    bool HasProjectBrief,
    bool HasCapabilityOverview,
    bool HasFullDescription,
    int ProjectBriefWordCount,
    int CapabilityOverviewWordCount,
    int FullDescriptionWordCount,
    int? DefaultPrimaryPhotoId,
    IReadOnlyList<BrochurePhotoOptionVm> Photos);

public sealed record BrochureProjectSelection(
    int ProjectId,
    int? PrimaryPhotoId,
    int? SecondaryPhotoId,
    double PrimaryFocalX,
    double PrimaryFocalY,
    double SecondaryFocalX,
    double SecondaryFocalY,
    BrochureImageMode ImageMode,
    bool PrimaryPhotoConfirmed = false,
    bool IsReviewed = false,
    string? ReviewFingerprint = null);

/// <summary>
/// Cover fields visible to the reviewer and therefore bound to a Cover B approval.
/// </summary>
public sealed record BrochureCoverReviewContext(
    string Title,
    string Subtitle,
    string Edition,
    string Strapline,
    string? HandlingMarking);

public sealed record BrochureProjectReviewFingerprint(
    int ProjectId,
    string Fingerprint);

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
    BrochurePublicationProfile PublicationProfile,
    string? IntroductionTitle,
    string? IntroductionText,
    string? HandlingMarking,
    string IssuerDisplayName,
    bool AllowTextOnlyProjects,
    DateTimeOffset GeneratedAtUtc,
    int? CoverHeroProjectId = null,
    int? CoverHeroPhotoId = null,
    double CoverHeroFocalX = .5d,
    double CoverHeroFocalY = .5d,
    bool IncludeBackCover = true,
    string? PrintIntroText = null,
    string? PrintFutureText = null,
    string? PrintProcurementText = null,
    string? PrintCentreStatement = null,
    string? PrintDevelopingAgencyText = null,
    string? PrintManufacturingAgencyText = null,
    string? PrintVisionaryText = null,
    string? PrintNewSimulatorsText = null,
    BrochureInstitutionalCoverArtwork InstitutionalCoverArtwork = BrochureInstitutionalCoverArtwork.ReferenceOriginal,
    bool RequirePublicationReview = false,
    bool CoverReviewed = false,
    string? CoverReviewFingerprint = null);

public sealed record BrochurePreflightIssue(
    BrochurePreflightIssueCode Code,
    PublicationIssueSeverity Severity,
    int? ProjectId,
    string? ProjectName,
    string Message);

public sealed record BrochurePreflight(
    int SelectedProjectCount,
    IReadOnlyList<BrochurePreflightIssue> Issues,
    int? ResolvedCoverHeroProjectId = null,
    int? ResolvedCoverHeroPhotoId = null,
    int ResolvedCoverHeroWidth = 0,
    int ResolvedCoverHeroHeight = 0,
    BrochurePhotoQuality? ResolvedCoverHeroQuality = null,
    int? EstimatedPageCount = null,
    int? EstimatedAveragePageUtilizationPercent = null,
    int? EstimatedClosingPageProjectCount = null,
    bool? ClosingMatterSharesFinalPage = null,
    int? LowestProjectPageUtilizationPercent = null,
    int? FinalPageUtilizationPercent = null,
    bool? PrintFrontPageUsesMinimumTypography = null,
    IReadOnlyList<BrochurePrintSheetSummary>? PrintSheetPlan = null,
    BrochurePrintFlowSuggestion? SmartFlowSuggestion = null,
    IReadOnlyList<BrochureProjectReviewFingerprint>? ProjectReviewFingerprints = null,
    string? CoverReviewFingerprint = null)
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
    BrochurePreflight Preflight,
    BrochurePublicationImage? CoverHeroImage = null);

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
