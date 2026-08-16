using System.Globalization;

namespace ProjectManagement.Services.Compendiums;

public enum CompendiumNarrativeSource
{
    ProjectBrief = 1,
    CapabilityOverview = 2,
    ProjectDescription = 3
}

public enum CompendiumGroupingMode
{
    TechnicalCategory = 1,
    None = 2,
    CustomSections = 3
}

public enum CompendiumSortMode
{
    Manual = 1,
    LatestFirst = 2,
    Alphabetical = 3
}

public enum CompendiumPhotoSelectionSource
{
    None = 0,
    ProjectCover = 1,
    MarkedCover = 2,
    FirstAvailable = 3,
    ExplicitPublication = 4
}

public enum CompendiumImageSelectionMode
{
    Automatic = 0,
    Explicit = 1
}

public enum CompendiumImageFitMode
{
    Fill = 0,
    Fit = 1
}

public enum CompendiumDossierLayout
{
    Automatic = 0,
    VisualHero = 1,
    Balanced = 2,
    MultiImageEditorial = 3,
    Technical = 4
}

public enum CompendiumBalancedTextFlowMode
{
    SideColumn = 0,
    FlowBelowImage = 1
}

/// <summary>
/// Publication narrative typesetting preference. The renderer may safely retain left alignment
/// in exceptionally narrow side columns while honouring Justified for full-width narrative.
/// </summary>
public enum CompendiumNarrativeAlignment
{
    Left = 0,
    Justified = 1
}

/// <summary>
/// Publication-level visual treatment for the recurring Project Particulars block. The style is
/// intentionally global to a Compendium so every dossier shares one coherent publication language.
/// </summary>
public enum CompendiumProjectParticularsStyle
{
    Panel = 0,
    Minimal = 1
}

public enum CompendiumDossierImageRole
{
    Primary = 0,
    Supporting1 = 1,
    Supporting2 = 2
}

public sealed record CompendiumDossierImageSelection(
    CompendiumDossierImageRole Role,
    int? PhotoId,
    double FocalX,
    double FocalY,
    CompendiumImageFitMode FitMode,
    CompendiumPhotoSelectionSource SelectionSource = CompendiumPhotoSelectionSource.None)
{
    /// <summary>Media version participates in review identity so a reprocessed/replaced image stales review.</summary>
    public int? PhotoVersion { get; init; }
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
}


public sealed record CompendiumIprCredentialDto(
    string Type,
    string Status,
    int? Year);

public sealed record CompendiumTechnologyTransferDto(
    string Status,
    int? CompletionYear);

public enum CompendiumCoverImageMode
{
    Automatic = 0,
    Explicit = 1,
    None = 2
}

public enum CompendiumFrontCoverTemplate
{
    InstitutionalHero = 0,
    FullBleedHero = 1,
    EditorialSplit = 2,
    Triptych = 3,
    Minimal = 4,
    PortfolioQuartet = 5
}

public enum CompendiumBackCoverTemplate
{
    MinimalInstitutional = 0,
    ImageEcho = 1,
    PortfolioStrip = 2,
    TypographyOnly = 3,
    Clean = 4
}

public enum CompendiumPublicationTheme
{
    InstitutionalGreen = 0,
    DeepNavy = 1,
    Burgundy = 2,
    Graphite = 3,
    DeepTeal = 4,
    Slate = 5
}

public enum CompendiumCoverBackgroundTreatment
{
    Solid = 0,
    SubtleGradient = 1,
    TopographicContours = 2,
    TechnicalGrid = 3,
    GeometricMesh = 4,
    Camouflage = 5
}

public enum CompendiumCoverSurface
{
    Front = 0,
    Back = 1
}

public enum CompendiumCoverLogoPlacement
{
    TopCorners = 0,
    TopCenter = 1
}

public sealed record CompendiumCoverImageSlot(
    CompendiumCoverSurface Surface,
    string SlotKey,
    CompendiumCoverImageMode ImageMode,
    int? ProjectId,
    int? PhotoId,
    double FocalX,
    double FocalY,
    CompendiumImageFitMode FitMode);

public sealed record CompendiumPhotoPreference(
    int ProjectId,
    int PhotoId,
    bool PreferredForPublication,
    bool SuitableForCoverHero);

public sealed record CompendiumCoverDesign(
    CompendiumFrontCoverTemplate FrontTemplate,
    CompendiumBackCoverTemplate BackTemplate,
    IReadOnlyList<CompendiumCoverImageSlot> Images)
{
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
    public IReadOnlyList<CompendiumPhotoPreference> PhotoPreferences { get; init; }
        = Array.Empty<CompendiumPhotoPreference>();
}

public sealed record CompendiumPublicationSection(
    string SectionKey,
    string Name,
    int SortOrder);

public enum CompendiumImageQuality
{
    Unknown = 0,
    Low = 1,
    Acceptable = 2,
    Good = 3
}

public enum CompendiumPublicationIssue
{
    MissingPhoto = 0,
    MissingSponsoringLineDirectorate = 1,
    MissingProliferationCost = 2,
    ZeroProliferationCost = 3,
    MissingDescription = 4,
    MissingCompletionYear = 5,
    PossibleTitleTypo = 6
}

public enum CompendiumFindingSeverity
{
    Information = 0,
    Warning = 1,
    Blocker = 2
}

public sealed record CompendiumFindingDto(
    CompendiumFindingSeverity Severity,
    string Code,
    string Message,
    int? ProjectId = null,
    string? ProjectName = null);

public sealed record CompendiumCandidateProjectVm(
    int ProjectId,
    string ProjectName,
    string Lifecycle,
    string? ProjectCategory,
    string? TechnicalCategory,
    bool IsAvailableForProliferation,
    bool HasDescription,
    bool HasSponsoringLineDirectorate,
    bool HasProliferationCost,
    int PhotoCount,
    int? DefaultPhotoId,
    string CompletionDisplay)
{
    public bool? ProliferationAvailability { get; init; }
    public bool HasProjectBrief { get; init; }
    public bool HasCapabilityOverview { get; init; }
    public int ProjectBriefWordCount { get; init; }
    public int CapabilityStatementCount { get; init; }
    public int DescriptionWordCount { get; init; }
    public int PublicationYear { get; init; }
    public string SponsoringLineDirectorateDisplay { get; init; } = "Not recorded";
    public string ProliferationCostDisplay { get; init; } = "Not recorded";
    public int TechnicalCategorySortOrder { get; init; } = int.MaxValue;
    public int TechnicalSpecificationCount { get; init; }
    public bool HasIpr { get; init; }
    public bool HasTechnologyTransfer { get; init; }
}

public sealed record CompendiumProjectSelection(
    int ProjectId,
    int? PrimaryPhotoId = null,
    double FocalX = .5d,
    double FocalY = .5d,
    CompendiumImageSelectionMode ImageSelectionMode = CompendiumImageSelectionMode.Automatic,
    string? ReviewFingerprint = null)
{
    /// <summary>
    /// Publication-only section assignment. It never modifies the project's authoritative
    /// Technical Category or any other PRISM master data.
    /// </summary>
    public string? CustomSectionKey { get; init; }
    public string? CustomSectionName { get; init; }
    public CompendiumNarrativeSource? NarrativeSourceOverride { get; init; }
    public CompendiumImageFitMode ImageFitMode { get; init; } = CompendiumImageFitMode.Fill;
    public CompendiumDossierLayout DossierLayout { get; init; } = CompendiumDossierLayout.Automatic;
    public CompendiumBalancedTextFlowMode BalancedTextFlowMode { get; init; } = CompendiumBalancedTextFlowMode.FlowBelowImage;
    /// <summary>Null inherits the publication-level narrative alignment.</summary>
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

public sealed record CompendiumReviewPhotoVm(
    int PhotoId,
    string? Caption,
    int Width,
    int Height,
    bool IsCover,
    bool IsLowResolution,
    int Version,
    bool IsUsable,
    string? SourceVariant,
    CompendiumImageQuality Quality);

public sealed record CompendiumReviewProjectDto(
    int ProjectId,
    string ProjectName,
    string LifecycleDisplay,
    string? ProjectCategoryName,
    string TechnicalCategoryName,
    string SponsoringLineDirectorateDisplay,
    string CompletionDisplay,
    bool? ProliferationAvailability,
    decimal? ProliferationCostLakhs,
    string ProliferationCostDisplay,
    string DescriptionMarkdown,
    IReadOnlyList<CompendiumReviewPhotoVm> Photos,
    int? ResolvedPhotoId,
    CompendiumPhotoSelectionSource PhotoSelectionSource,
    CompendiumImageSelectionMode ImageSelectionMode,
    double FocalX,
    double FocalY,
    int? EffectiveDpi,
    CompendiumImageQuality ImageQuality,
    string ReviewFingerprint,
    bool IsReviewed,
    bool IsReviewStale,
    bool ExplicitPhotoUnavailable)
{
    public double ImageFrameWidthPoints { get; init; } = CompendiumPublicationImagePolicy.FrameWidthPoints;
    public double ImageFrameHeightPoints { get; init; } = CompendiumPublicationImagePolicy.MediumFrameHeightPoints;
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectBrief;
    public string NarrativeLabel { get; init; } = "Project Brief";
    public bool HasProjectBrief { get; init; }
    public bool HasCapabilityOverview { get; init; }
    public bool HasProjectDescription { get; init; }
    public int ProjectBriefWordCount { get; init; }
    public int CapabilityStatementCount { get; init; }
    public int DescriptionWordCount { get; init; }
    public string? CustomSectionKey { get; init; }
    public string? CustomSectionName { get; init; }
    public bool UsesNarrativeOverride { get; init; }
    public CompendiumImageFitMode ImageFitMode { get; init; } = CompendiumImageFitMode.Fill;
    public IReadOnlyList<CompendiumProgrammeModuleDto> ProgrammeModules { get; init; } = Array.Empty<CompendiumProgrammeModuleDto>();
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public IReadOnlyList<CompendiumIprCredentialDto> IprCredentials { get; init; } = Array.Empty<CompendiumIprCredentialDto>();
    public CompendiumTechnologyTransferDto? TechnologyTransfer { get; init; }
    public IReadOnlyList<string> TechnicalSpecifications { get; init; } = Array.Empty<string>();
    public CompendiumDossierLayout DossierLayoutOverride { get; init; } = CompendiumDossierLayout.Automatic;
    public CompendiumDossierLayout EffectiveDossierLayout { get; init; } = CompendiumDossierLayout.Balanced;
    public string DossierLayoutReason { get; init; } = string.Empty;
    public int DossierPressureScore { get; init; }
    public float DossierPrimaryImageHeightPoints { get; init; } = 246f;
    public float DossierNarrativeFontScale { get; init; } = 1f;
    public int DossierFirstPageNarrativeBudget { get; init; } = 2200;
    public float DossierFirstPageNarrativeHeightPoints { get; init; } = 610f;
    public int DossierFirstPageSpecificationCount { get; init; } = 6;
    public int DossierSpecificationColumns { get; init; } = 1;
    public int DossierProgrammeColumns { get; init; } = 1;
    public CompendiumBalancedTextFlowMode BalancedTextFlowMode { get; init; } = CompendiumBalancedTextFlowMode.FlowBelowImage;
    public CompendiumNarrativeAlignment NarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public bool UsesNarrativeAlignmentOverride { get; init; }
    public string? AdditionalNote { get; init; }
    public int AdditionalNoteCharacterCount { get; init; }
    public float AdditionalNoteMeasuredHeightPoints { get; init; }
    public CompendiumDossierNarrativeFlowPlan NarrativeFlow { get; init; } = CompendiumDossierNarrativeFlowPlan.Empty;
    public int EstimatedDossierPageCount { get; init; } = 1;
    public string DossierPaginationNote { get; init; } = "1 dossier page";
    public string DossierPaginationReason { get; init; } = string.Empty;
    public string? DossierEditorialWarning { get; init; }
    public int DossierImageCount { get; init; } = 1;
    public IReadOnlyList<CompendiumDossierImageSelection> DossierImages { get; init; } = Array.Empty<CompendiumDossierImageSelection>();
}

public sealed record CompendiumProjectDto(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string TechnicalCategoryName,
    int? CompletionYearValue,
    string CompletionYearDisplay,
    string SponsoringLineDirectorateDisplay,
    decimal? ProliferationCostLakhs,
    string? ProliferationCostRemarks,
    int? CoverPhotoId,
    CompendiumPhotoSelectionSource CoverPhotoSource,
    string DescriptionMarkdown,
    IReadOnlyList<CompendiumPublicationIssue> PublicationIssues)
{
    public string LifecycleDisplay { get; init; } = "Completed";
    public string? ProjectCategoryName { get; init; }
    public bool IsAvailableForProliferation { get; init; }
    public bool? ProliferationAvailability { get; init; }
    public int PhotoCount { get; init; }
    public int SortOrder { get; init; }
    public CompendiumImageSelectionMode ImageSelectionMode { get; init; } = CompendiumImageSelectionMode.Automatic;
    public double PrimaryFocalX { get; init; } = .5d;
    public double PrimaryFocalY { get; init; } = .5d;
    public int? EffectiveDpi { get; init; }
    public CompendiumImageQuality ImageQuality { get; init; } = CompendiumImageQuality.Unknown;
    public string ReviewFingerprint { get; init; } = string.Empty;
    public bool IsReviewed { get; init; }
    public bool IsReviewStale { get; init; }
    public bool ExplicitPhotoUnavailable { get; init; }
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectBrief;
    public string NarrativeLabel { get; init; } = "Project Brief";
    public string? CustomSectionKey { get; init; }
    public string? CustomSectionName { get; init; }
    public bool UsesNarrativeOverride { get; init; }
    public int PublicationYear { get; init; }
    public int TechnicalCategorySortOrder { get; init; } = int.MaxValue;
    public CompendiumImageFitMode ImageFitMode { get; init; } = CompendiumImageFitMode.Fill;
    public IReadOnlyList<CompendiumProgrammeModuleDto> ProgrammeModules { get; init; } = Array.Empty<CompendiumProgrammeModuleDto>();
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public IReadOnlyList<CompendiumIprCredentialDto> IprCredentials { get; init; } = Array.Empty<CompendiumIprCredentialDto>();
    public CompendiumTechnologyTransferDto? TechnologyTransfer { get; init; }
    public IReadOnlyList<string> TechnicalSpecifications { get; init; } = Array.Empty<string>();
    public CompendiumDossierLayout DossierLayoutOverride { get; init; } = CompendiumDossierLayout.Automatic;
    public CompendiumDossierLayout EffectiveDossierLayout { get; init; } = CompendiumDossierLayout.Balanced;
    public string DossierLayoutReason { get; init; } = string.Empty;
    public int DossierPressureScore { get; init; }
    public float DossierPrimaryImageHeightPoints { get; init; } = 246f;
    public float DossierNarrativeFontScale { get; init; } = 1f;
    public int DossierFirstPageNarrativeBudget { get; init; } = 2200;
    public float DossierFirstPageNarrativeHeightPoints { get; init; } = 610f;
    public int DossierFirstPageSpecificationCount { get; init; } = 6;
    public int DossierSpecificationColumns { get; init; } = 1;
    public int DossierProgrammeColumns { get; init; } = 1;
    public CompendiumBalancedTextFlowMode BalancedTextFlowMode { get; init; } = CompendiumBalancedTextFlowMode.FlowBelowImage;
    public CompendiumNarrativeAlignment NarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public bool UsesNarrativeAlignmentOverride { get; init; }
    public string? AdditionalNote { get; init; }
    public int AdditionalNoteCharacterCount { get; init; }
    public float AdditionalNoteMeasuredHeightPoints { get; init; }
    public CompendiumDossierNarrativeFlowPlan NarrativeFlow { get; init; } = CompendiumDossierNarrativeFlowPlan.Empty;
    public int EstimatedDossierPageCount { get; init; } = 1;
    public string DossierPaginationNote { get; init; } = "1 dossier page";
    public string DossierPaginationReason { get; init; } = string.Empty;
    public string? DossierEditorialWarning { get; init; }
    public int DossierImageCount { get; init; } = 1;
    public IReadOnlyList<CompendiumDossierImageSelection> DossierImages { get; init; } = Array.Empty<CompendiumDossierImageSelection>();
}

/// <summary>
/// A publication section. The legacy property name is retained for source compatibility; when
/// custom grouping is selected it contains the publication-only custom section name.
/// </summary>
public sealed record CompendiumCategoryGroupDto(
    string TechnicalCategoryName,
    IReadOnlyList<CompendiumProjectDto> Projects)
{
    public string SectionName => TechnicalCategoryName;
}

public sealed record CompendiumProjectReadinessDto(
    int ProjectId,
    string ProjectName,
    string TechnicalCategoryName,
    string CompletionYearDisplay,
    IReadOnlyList<CompendiumPublicationIssue> Issues)
{
    public bool HasWarnings => Issues.Count > 0;
}

public sealed record CompendiumPreflightDto(
    int CompletedProjectCount,
    int EligibleProjectCount,
    int CategoryCount,
    int ExcludedNotAvailableCount,
    int MissingAvailabilityStatusCount,
    int PhotoSelectedCount,
    int MissingPhotoCount,
    int MissingSponsoringLineDirectorateCount,
    int MissingCostCount,
    int ZeroCostCount,
    int MissingDescriptionCount,
    int MissingCompletionYearCount,
    int PossibleTitleTypoCount,
    IReadOnlyList<CompendiumProjectReadinessDto> Projects)
{
    public static CompendiumPreflightDto Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        Array.Empty<CompendiumProjectReadinessDto>());

    public int CandidateProjectCount { get; init; }
    public int SelectedProjectCount { get; init; }
    public int BlockerCount { get; init; }
    public int InformationCount { get; init; }
    public int? WarningCount { get; init; }
    public IReadOnlyList<CompendiumFindingDto> Findings { get; init; } = Array.Empty<CompendiumFindingDto>();

    public int TotalWarningCount => WarningCount ??
        (MissingPhotoCount
         + MissingSponsoringLineDirectorateCount
         + MissingCostCount
         + ZeroCostCount
         + MissingDescriptionCount
         + MissingCompletionYearCount
         + PossibleTitleTypoCount);

    public int ProjectsWithWarnings => Projects.Count(project => project.HasWarnings);
    public bool CanGenerate => SelectedProjectCount > 0 ? BlockerCount == 0 : EligibleProjectCount > 0;
    public bool IsPublicationReady => CanGenerate && TotalWarningCount == 0 && BlockerCount == 0;
}

public sealed record CompendiumPublicationRequest(
    IReadOnlyList<CompendiumProjectSelection> Projects,
    string? Title = null,
    string? Subtitle = null,
    string? Edition = null)
{
    public CompendiumPublicationRequest(
        IReadOnlyList<int> projectIds,
        string? title = null,
        string? subtitle = null,
        string? edition = null)
        : this(
            projectIds?
                .Where(projectId => projectId > 0)
                .Select(projectId => new CompendiumProjectSelection(projectId))
                .ToArray()
            ?? Array.Empty<CompendiumProjectSelection>(),
            title,
            subtitle,
            edition)
    {
    }

    public IReadOnlyList<int> ProjectIds => Projects.Select(project => project.ProjectId).ToArray();
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectBrief;
    public CompendiumNarrativeAlignment DefaultNarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public CompendiumGroupingMode GroupingMode { get; init; } = CompendiumGroupingMode.TechnicalCategory;
    public CompendiumSortMode SortMode { get; init; } = CompendiumSortMode.Manual;
    public IReadOnlyList<CompendiumPublicationSection> Sections { get; init; } = Array.Empty<CompendiumPublicationSection>();
    public CompendiumCoverDesign? CoverDesign { get; init; }
    public IReadOnlyList<CompendiumPhotoPreference> PhotoPreferences { get; init; } = Array.Empty<CompendiumPhotoPreference>();
}

public sealed record CompendiumPdfDataDto(
    string Title,
    string Subtitle,
    string UnitDisplayName,
    string IssuerDisplayName,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CompendiumCategoryGroupDto> Groups,
    CompendiumPreflightDto Preflight)
{
    public string Edition { get; init; } = string.Empty;
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectBrief;
    public CompendiumNarrativeAlignment DefaultNarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public CompendiumGroupingMode GroupingMode { get; init; } = CompendiumGroupingMode.TechnicalCategory;
    public CompendiumSortMode SortMode { get; init; } = CompendiumSortMode.Manual;
    public CompendiumCoverDesign? CoverDesign { get; init; }
}

public static class CompendiumPublicationImagePolicy
{
    public const double FrameWidthPoints = 519d;
    public const double LongFrameHeightPoints = 190d;
    public const double MediumFrameHeightPoints = 240d;
    public const double ShortFrameHeightPoints = 300d;
    // Medium aliases preserve the existing browser/bootstrap contract while Phase 24.1 sends
    // exact per-project geometry with each review payload.
    public const double FrameHeightPoints = MediumFrameHeightPoints;
    public const int RenderWidthPixels = 1800;
    public const int RenderHeightPixels = 832;
    public const int GoodDpi = 180;
    public const int AcceptableDpi = 150;

    public static double ResolveFrameHeightPoints(string? descriptionMarkdown)
    {
        var lines = EstimateNarrativeLines(descriptionMarkdown);
        if (lines <= 6) return ShortFrameHeightPoints;
        if (lines <= 18) return MediumFrameHeightPoints;
        return LongFrameHeightPoints;
    }

    /// <summary>
    /// Estimates rendered narrative pressure rather than using raw character count. Markdown
    /// markers, list prefixes and paragraph boundaries are normalised so layout choice more
    /// closely tracks the number of visual lines that QuestPDF will consume.
    /// </summary>
    public static int EstimateNarrativeLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var total = 0d;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                total += .45d;
                continue;
            }

            line = line.Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal)
                .TrimStart('#', '>', ' ', '\t');
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                line = line[2..].Trim();
                total += .2d;
            }
            else
            {
                var prefix = 0;
                while (prefix < line.Length && char.IsDigit(line[prefix])) prefix++;
                if (prefix > 0 && prefix + 1 < line.Length && (line[prefix] == '.' || line[prefix] == ')'))
                {
                    line = line[(prefix + 1)..].Trim();
                    total += .2d;
                }
            }

            // At 10 pt on the 519 pt dossier text width, normal prose averages roughly
            // 88-96 characters per visual line. 90 keeps the estimate conservative for
            // military abbreviations and bold fragments without over-penalising short words.
            total += Math.Max(1d, Math.Ceiling(Math.Max(1, line.Length) / 90d));
        }

        return Math.Max(1, (int)Math.Ceiling(total));
    }

    public static int ResolveRenderHeightPixels(string? descriptionMarkdown)
        => (int)Math.Round(RenderWidthPixels * ResolveFrameHeightPoints(descriptionMarkdown) / FrameWidthPoints);

    public static int? CalculateEffectiveDpi(
        int sourceWidth,
        int sourceHeight,
        string? descriptionMarkdown = null,
        CompendiumImageFitMode fitMode = CompendiumImageFitMode.Fill)
        => CalculateEffectiveDpi(
            sourceWidth,
            sourceHeight,
            FrameWidthPoints,
            ResolveFrameHeightPoints(descriptionMarkdown),
            fitMode);

    public static int? CalculateEffectiveDpi(
        int sourceWidth,
        int sourceHeight,
        double frameWidthPoints,
        double frameHeightPoints,
        CompendiumImageFitMode fitMode = CompendiumImageFitMode.Fill)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || frameWidthPoints <= 0 || frameHeightPoints <= 0) return null;
        var targetAspect = frameWidthPoints / frameHeightPoints;
        var sourceAspect = sourceWidth / (double)sourceHeight;

        if (fitMode == CompendiumImageFitMode.Fit)
        {
            double displayWidthPoints;
            double displayHeightPoints;
            if (sourceAspect >= targetAspect)
            {
                displayWidthPoints = frameWidthPoints;
                displayHeightPoints = frameWidthPoints / sourceAspect;
            }
            else
            {
                displayHeightPoints = frameHeightPoints;
                displayWidthPoints = frameHeightPoints * sourceAspect;
            }

            var horizontalFitDpi = sourceWidth / (displayWidthPoints / 72d);
            var verticalFitDpi = sourceHeight / (displayHeightPoints / 72d);
            return (int)Math.Floor(Math.Min(horizontalFitDpi, verticalFitDpi));
        }

        double cropWidth;
        double cropHeight;
        if (sourceAspect > targetAspect)
        {
            cropHeight = sourceHeight;
            cropWidth = cropHeight * targetAspect;
        }
        else
        {
            cropWidth = sourceWidth;
            cropHeight = cropWidth / targetAspect;
        }

        var horizontalDpi = cropWidth / (frameWidthPoints / 72d);
        var verticalDpi = cropHeight / (frameHeightPoints / 72d);
        return (int)Math.Floor(Math.Min(horizontalDpi, verticalDpi));
    }

    public static CompendiumImageQuality Classify(int? dpi)
        => dpi switch
        {
            null => CompendiumImageQuality.Unknown,
            < AcceptableDpi => CompendiumImageQuality.Low,
            < GoodDpi => CompendiumImageQuality.Acceptable,
            _ => CompendiumImageQuality.Good
        };

    public static string FormatCost(decimal? value)
        => value.HasValue
            ? $"₹{value.Value.ToString("0.##", CultureInfo.InvariantCulture)} lakh"
            : "Not recorded";

}


/// <summary>
/// Fixed geometry for the Compendium cover hero. Project dossier imagery is adaptive,
/// while the publication cover intentionally uses one stable editorial frame.
/// </summary>
public static class CompendiumCoverImagePolicy
{
    public const double FrameWidthPoints = 491d;
    public const double FrameHeightPoints = 300d;
    public const int RenderWidthPixels = 1800;
    public const int RenderHeightPixels = 1100;

    public static double TargetAspect => FrameWidthPoints / FrameHeightPoints;
}
