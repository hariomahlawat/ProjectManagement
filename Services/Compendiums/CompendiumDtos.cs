using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Compendiums;

public enum CompendiumPhotoSelectionSource
{
    None = 0,
    ExplicitCover = 1,
    MarkedCover = 2,
    FirstAvailable = 3
}

public enum CompendiumPublicationIssue
{
    MissingPhoto = 1,
    MissingArmService = 2,
    MissingProliferationCost = 3,
    ZeroProliferationCost = 4,
    MissingDescription = 5,
    MissingCompletionYear = 6,
    PossibleTitleTypo = 7
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
    IReadOnlyList<CompendiumPublicationIssue> PublicationIssues);

public sealed record CompendiumCategoryGroupDto(
    string TechnicalCategoryName,
    IReadOnlyList<CompendiumProjectDto> Projects);

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
        CompletedProjectCount: 0,
        EligibleProjectCount: 0,
        CategoryCount: 0,
        ExcludedNotAvailableCount: 0,
        MissingAvailabilityStatusCount: 0,
        PhotoSelectedCount: 0,
        MissingPhotoCount: 0,
        MissingArmServiceCount: 0,
        MissingCostCount: 0,
        ZeroCostCount: 0,
        MissingDescriptionCount: 0,
        MissingCompletionYearCount: 0,
        PossibleTitleTypoCount: 0,
        Projects: Array.Empty<CompendiumProjectReadinessDto>());

    public int ProjectsWithWarnings => Projects.Count(project => project.HasWarnings);

    public int TotalWarningCount =>
        MissingPhotoCount
        + MissingArmServiceCount
        + MissingCostCount
        + ZeroCostCount
        + MissingDescriptionCount
        + MissingCompletionYearCount
        + PossibleTitleTypoCount;

    public bool CanGenerate => EligibleProjectCount > 0;

    public bool IsPublicationReady => CanGenerate && TotalWarningCount == 0;
}

public sealed record CompendiumPdfDataDto(
    string Title,
    string Subtitle,
    string UnitDisplayName,
    string IssuerDisplayName,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CompendiumCategoryGroupDto> Groups,
    CompendiumPreflightDto Preflight);
