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

public enum CompendiumCoverImageMode
{
    Automatic = 0,
    Explicit = 1,
    None = 2
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
    MissingArmService = 1,
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
    bool HasArmService,
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
    string ArmServiceDisplay,
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
}

public sealed record CompendiumProjectDto(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string TechnicalCategoryName,
    int? CompletionYearValue,
    string CompletionYearDisplay,
    string ArmServiceDisplay,
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
    int MissingArmServiceCount,
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
         + MissingArmServiceCount
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
    public CompendiumGroupingMode GroupingMode { get; init; } = CompendiumGroupingMode.TechnicalCategory;
    public CompendiumSortMode SortMode { get; init; } = CompendiumSortMode.Manual;
    public IReadOnlyList<CompendiumPublicationSection> Sections { get; init; } = Array.Empty<CompendiumPublicationSection>();
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
    public CompendiumGroupingMode GroupingMode { get; init; } = CompendiumGroupingMode.TechnicalCategory;
    public CompendiumSortMode SortMode { get; init; } = CompendiumSortMode.Manual;
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
        var length = PublicationTextLength(descriptionMarkdown);
        if (length <= 220) return ShortFrameHeightPoints;
        if (length <= 1050) return MediumFrameHeightPoints;
        return LongFrameHeightPoints;
    }

    public static int ResolveRenderHeightPixels(string? descriptionMarkdown)
        => (int)Math.Round(RenderWidthPixels * ResolveFrameHeightPoints(descriptionMarkdown) / FrameWidthPoints);

    public static int? CalculateEffectiveDpi(int sourceWidth, int sourceHeight, string? descriptionMarkdown = null)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return null;
        var frameHeight = ResolveFrameHeightPoints(descriptionMarkdown);
        var targetAspect = FrameWidthPoints / frameHeight;
        var sourceAspect = sourceWidth / (double)sourceHeight;
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

        var horizontalDpi = cropWidth / (FrameWidthPoints / 72d);
        var verticalDpi = cropHeight / (frameHeight / 72d);
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

    private static int PublicationTextLength(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? 0
            : value.Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal)
                .Trim().Length;
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
