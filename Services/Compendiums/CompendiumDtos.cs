namespace ProjectManagement.Services.Compendiums;

public enum CompendiumPhotoSelectionSource { None = 0, ExplicitCover = 1, MarkedCover = 2, FirstAvailable = 3 }
public enum CompendiumPublicationIssue { MissingPhoto = 0, MissingArmService = 1, MissingProliferationCost = 2, ZeroProliferationCost = 3, MissingDescription = 4, MissingCompletionYear = 5, PossibleTitleTypo = 6 }
public enum CompendiumFindingSeverity { Information = 0, Warning = 1, Blocker = 2 }

public sealed record CompendiumFindingDto(CompendiumFindingSeverity Severity, string Code, string Message, int? ProjectId = null, string? ProjectName = null);

public sealed record CompendiumCandidateProjectVm(
    int ProjectId, string ProjectName, string Lifecycle, string? ProjectCategory, string? TechnicalCategory,
    bool IsAvailableForProliferation, bool HasDescription, bool HasArmService, bool HasProliferationCost,
    int PhotoCount, int? DefaultPhotoId, string CompletionDisplay);

public sealed record CompendiumProjectDto(
    int ProjectId, string ProjectName, string? CaseFileNumber, string TechnicalCategoryName,
    int? CompletionYearValue, string CompletionYearDisplay, string ArmServiceDisplay,
    decimal? ProliferationCostLakhs, string? ProliferationCostRemarks, int? CoverPhotoId,
    CompendiumPhotoSelectionSource CoverPhotoSource, string DescriptionMarkdown,
    IReadOnlyList<CompendiumPublicationIssue> PublicationIssues)
{
    public string LifecycleDisplay { get; init; } = "Completed";
    public string? ProjectCategoryName { get; init; }
    public bool IsAvailableForProliferation { get; init; }
    public int PhotoCount { get; init; }
    public int SortOrder { get; init; }
}

public sealed record CompendiumCategoryGroupDto(string TechnicalCategoryName, IReadOnlyList<CompendiumProjectDto> Projects);
public sealed record CompendiumProjectReadinessDto(int ProjectId, string ProjectName, string TechnicalCategoryName, string CompletionYearDisplay, IReadOnlyList<CompendiumPublicationIssue> Issues)
{ public bool HasWarnings => Issues.Count > 0; }

public sealed record CompendiumPreflightDto(
    int CompletedProjectCount, int EligibleProjectCount, int CategoryCount, int ExcludedNotAvailableCount,
    int MissingAvailabilityStatusCount, int PhotoSelectedCount, int MissingPhotoCount, int MissingArmServiceCount,
    int MissingCostCount, int ZeroCostCount, int MissingDescriptionCount, int MissingCompletionYearCount,
    int PossibleTitleTypoCount, IReadOnlyList<CompendiumProjectReadinessDto> Projects)
{
    public static CompendiumPreflightDto Empty { get; } = new(0,0,0,0,0,0,0,0,0,0,0,0,0,Array.Empty<CompendiumProjectReadinessDto>());
    public int CandidateProjectCount { get; init; }
    public int SelectedProjectCount { get; init; }
    public int BlockerCount { get; init; }
    public int InformationCount { get; init; }
    public IReadOnlyList<CompendiumFindingDto> Findings { get; init; } = Array.Empty<CompendiumFindingDto>();
    public int TotalWarningCount => MissingPhotoCount + MissingArmServiceCount + MissingCostCount + ZeroCostCount + MissingDescriptionCount + MissingCompletionYearCount + PossibleTitleTypoCount;
    public int ProjectsWithWarnings => Projects.Count(p => p.HasWarnings);
    public bool CanGenerate => SelectedProjectCount > 0 ? BlockerCount == 0 : EligibleProjectCount > 0;
    public bool IsPublicationReady => CanGenerate && TotalWarningCount == 0 && BlockerCount == 0;
}

public sealed record CompendiumPublicationRequest(IReadOnlyList<int> ProjectIds, string? Title = null, string? Subtitle = null, string? Edition = null);

public sealed record CompendiumPdfDataDto(
    string Title, string Subtitle, string UnitDisplayName, string IssuerDisplayName,
    DateTimeOffset GeneratedAtUtc, IReadOnlyList<CompendiumCategoryGroupDto> Groups, CompendiumPreflightDto Preflight)
{
    public string Edition { get; init; } = string.Empty;
}
