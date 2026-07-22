using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

public enum ProjectBriefingSelectionKind
{
    Ongoing = 1,
    RecentlyCompleted = 2,
    ProjectCategory = 3,
    TechnicalCategory = 4,
    AvailableForProliferation = 5,
    IndividualProjects = 6
}

public enum ProjectBriefingCostBasis
{
    None = 0,
    L1 = 1,
    AoN = 2,
    IPA = 3,
    Proliferation = 4
}

public sealed record ProjectBriefingCostValue(
    decimal? AmountInRupees,
    ProjectBriefingCostBasis Basis,
    string DisplayValue,
    string BasisDisplay)
{
    public bool IsAvailable => AmountInRupees is > 0m;

    public static ProjectBriefingCostValue Missing(ProjectBriefingCostBasis basis = ProjectBriefingCostBasis.None)
        => new(null, basis, "Not recorded", basis == ProjectBriefingCostBasis.Proliferation ? "Proliferation" : string.Empty);
}

public sealed record ProjectBriefingDeckSummaryVm(
    long Id,
    string Name,
    string? Description,
    ProjectBriefingPresentationMode PresentationMode,
    ProjectBriefingCostMode CostMode,
    int ProjectCount,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastGeneratedAtUtc,
    string CreatedByDisplay,
    string LastModifiedByDisplay,
    string RowVersion);

public sealed class ProjectBriefingDeckVm
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ProjectBriefingPresentationMode PresentationMode { get; init; }
    public ProjectBriefingCostMode CostMode { get; init; }
    public ProjectBriefingPresentationTheme PresentationTheme { get; init; }
        = ProjectBriefingPresentationTheme.EditorialLight;
    public ProjectBriefingBrandingScope BrandingScope { get; init; }
        = ProjectBriefingBrandingScope.AllSlides;
    public bool IncludeStageSummary { get; init; }
    public bool IncludeProjectCategorySummary { get; init; }
    public bool IncludeTechnicalCategorySummary { get; init; }
    public string? HandlingMarking { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string CreatedByDisplay { get; init; } = string.Empty;
    public string LastModifiedByDisplay { get; init; } = string.Empty;
    public IReadOnlyList<ProjectBriefingProjectVm> Projects { get; init; } = Array.Empty<ProjectBriefingProjectVm>();
    public ProjectBriefingReadinessVm Readiness { get; init; } = new();
    public ProjectBriefingSlideEstimateVm SlideEstimate { get; init; } = new();
}

public sealed class ProjectBriefingProjectVm
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string LifecycleDisplay { get; init; } = string.Empty;
    public ProjectLifecycleStatus LifecycleStatus { get; init; }
    public string PresentStageCode { get; init; } = string.Empty;
    public string PresentStage { get; init; } = string.Empty;
    public int PresentStageOrder { get; init; } = ProjectBriefingStageOrder.Unknown;
    public string? ProjectCategory { get; init; }
    public string? TechnicalCategory { get; init; }
    public ProjectBriefingCostValue CostRd { get; init; } = ProjectBriefingCostValue.Missing();
    public ProjectBriefingCostValue ProliferationCost { get; init; } = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation);
    public string? ExternalStatus { get; init; }
    public DateOnly? ExternalStatusDate { get; init; }
    // HasCoverPhoto now means the selected photograph is actually readable and PowerPoint-ready.
    public bool HasCoverPhoto { get; init; }
    public bool HasSelectedCoverPhoto { get; init; }
    public int? CoverPhotoId { get; init; }
    public string? CoverPhotoReadinessReason { get; init; }
    public string BriefDescription { get; init; } = string.Empty;
    public string? BriefDescriptionOverride { get; init; }
    public int SortOrder { get; init; }
    public string OpenUrl { get; init; } = string.Empty;
}

public sealed class ProjectBriefingReadinessVm
{
    public int ProjectCount { get; init; }
    public int OngoingCount { get; init; }
    public int CompletedCount { get; init; }
    public int ExternalStatusAvailableCount { get; init; }
    public int CostRdAvailableCount { get; init; }
    public int ProliferationCostAvailableCount { get; init; }
    public int CoverPhotoAvailableCount { get; init; }
    public int SelectedCoverPhotoCount { get; init; }
    public int DescriptionAvailableCount { get; init; }
}

public sealed class ProjectBriefingSlideEstimateVm
{
    public int TotalSlides { get; init; }
    public int CoverAndPortfolioSlides { get; init; }
    public int SummarySlides { get; init; }
    public int ExecutiveTableSlides { get; init; }
    public int DetailedProjectSlides { get; init; }
    public int CapabilityContinuationSlides { get; init; }
}

public sealed record ProjectBriefingLookupOptionVm(int Id, string Name, int MatchCount = 0, int? ParentId = null);

public sealed class ProjectBriefingSelectionOptionsVm
{
    public int OngoingCount { get; init; }
    public int CompletedCount { get; init; }
    public int ProliferationAvailableCount { get; init; }
    public int MinimumCompletionYear { get; init; }
    public int MaximumCompletionYear { get; init; }
    public IReadOnlyList<ProjectBriefingLookupOptionVm> ProjectCategories { get; init; }
        = Array.Empty<ProjectBriefingLookupOptionVm>();
    public IReadOnlyList<ProjectBriefingLookupOptionVm> TechnicalCategories { get; init; }
        = Array.Empty<ProjectBriefingLookupOptionVm>();
}

public sealed class ProjectBriefingSelectionRequest
{
    public ProjectBriefingSelectionKind Kind { get; init; }
    public IReadOnlyList<int> ProjectCategoryIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> TechnicalCategoryIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> ProjectIds { get; init; } = Array.Empty<int>();
    public int? CompletionYearFrom { get; init; }
    public int? CompletionYearTo { get; init; }
}

public sealed record ProjectBriefingSelectionResult(
    IReadOnlyList<int> ProjectIds,
    string RuleSummary,
    string SelectionRulesJson);

public sealed record ProjectBriefingSearchResultVm(
    int ProjectId,
    string ProjectName,
    string Lifecycle,
    string PresentStage,
    string? ProjectCategory,
    string? TechnicalCategory,
    string? ProjectOfficer,
    string? CaseFileNumber);

public sealed record ProjectBriefingManageSearchResultVm(
    int ProjectId,
    string ProjectName,
    string Lifecycle,
    string PresentStage,
    string? ProjectCategory,
    string? TechnicalCategory,
    string? ProjectOfficer,
    string? CaseFileNumber,
    bool IsSelected,
    int? SortOrder);

public sealed record ProjectBriefingMembershipUpdateResult(
    string RowVersion,
    int AddedCount,
    int RemovedCount);

public sealed class ProjectBriefingDeckSettingsCommand
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ProjectBriefingPresentationMode PresentationMode { get; init; }
    public ProjectBriefingCostMode CostMode { get; init; }
    public ProjectBriefingPresentationTheme PresentationTheme { get; init; }
    public ProjectBriefingBrandingScope BrandingScope { get; init; }
    public bool IncludeStageSummary { get; init; }
    public bool IncludeProjectCategorySummary { get; init; }
    public bool IncludeTechnicalCategorySummary { get; init; }
    public string? HandlingMarking { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ProjectBriefingExportResult(
    byte[] Content,
    string ContentType,
    string FileName,
    int SlideCount);

public sealed class ProjectBriefingPresentationData
{
    public long DeckId { get; init; }
    public string DeckName { get; init; } = string.Empty;
    public string? DeckDescription { get; init; }
    public ProjectBriefingPresentationMode PresentationMode { get; init; }
    public ProjectBriefingCostMode CostMode { get; init; }
    public ProjectBriefingPresentationTheme PresentationTheme { get; init; }
        = ProjectBriefingPresentationTheme.EditorialLight;
    public ProjectBriefingBrandingScope BrandingScope { get; init; }
        = ProjectBriefingBrandingScope.AllSlides;
    public bool IncludeStageSummary { get; init; }
    public bool IncludeProjectCategorySummary { get; init; }
    public bool IncludeTechnicalCategorySummary { get; init; }
    public string? HandlingMarking { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyList<ProjectBriefingPresentationProject> Projects { get; init; }
        = Array.Empty<ProjectBriefingPresentationProject>();
    public ProjectBriefingPresentationSummary Summary { get; init; } = new();
}

public sealed class ProjectBriefingPresentationProject
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public ProjectLifecycleStatus LifecycleStatus { get; init; }
    public string LifecycleDisplay { get; init; } = string.Empty;
    public string PresentStageCode { get; init; } = string.Empty;
    public string PresentStage { get; init; } = string.Empty;
    public int PresentStageOrder { get; init; }
    public string? ProjectCategory { get; init; }
    public string? TechnicalCategory { get; init; }
    public ProjectBriefingCostValue CostRd { get; init; } = ProjectBriefingCostValue.Missing();
    public ProjectBriefingCostValue ProliferationCost { get; init; } = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation);
    public string ExternalStatus { get; init; } = "No external status recorded";
    public DateOnly? ExternalStatusDate { get; init; }
    public string BriefDescription { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public int? CoverPhotoId { get; init; }
    public bool CoverPhotoIsReady { get; init; }
    public byte[]? CoverPhoto { get; set; }
    public string? CoverPhotoContentType { get; set; }
}

public sealed class ProjectBriefingPresentationSummary
{
    public int ProjectCount { get; init; }
    public int OngoingCount { get; init; }
    public int CompletedCount { get; init; }
    public decimal TotalCostRdInRupees { get; init; }
    public int CostRdRecordedCount { get; init; }
    public decimal TotalProliferationCostInRupees { get; init; }
    public int ProliferationCostRecordedCount { get; init; }
    public int MissingExternalStatusCount { get; init; }
    public int MissingPhotoCount { get; init; }
    public IReadOnlyList<ProjectBriefingSummaryPoint> StageSummary { get; init; } = Array.Empty<ProjectBriefingSummaryPoint>();
    public IReadOnlyList<ProjectBriefingSummaryPoint> ProjectCategorySummary { get; init; } = Array.Empty<ProjectBriefingSummaryPoint>();
    public IReadOnlyList<ProjectBriefingSummaryPoint> TechnicalCategorySummary { get; init; } = Array.Empty<ProjectBriefingSummaryPoint>();
}

public sealed record ProjectBriefingSummaryPoint(string Label, int Count, int Order = int.MaxValue);

public sealed record ProjectBriefingPhotoReference(int ProjectId, int PhotoId);

public sealed record ProjectBriefingPhotoProbe(
    int ProjectId,
    int PhotoId,
    bool IsReady,
    string? FailureReason = null);

public sealed record ProjectBriefingPhotoContent(
    int ProjectId,
    int PhotoId,
    byte[] Content,
    string ContentType,
    string SourceVariant);
