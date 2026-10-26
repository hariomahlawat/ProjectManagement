namespace ProjectManagement.Models.Analytics;

// SECTION: Ongoing analytics view model
public sealed class OngoingAnalyticsVm
{
    public int TotalOngoingProjects { get; init; }

    public IReadOnlyList<OngoingStageByParentCategoryPoint> ByStageByParentCategory { get; init; } =
        Array.Empty<OngoingStageByParentCategoryPoint>();

    public IReadOnlyList<OngoingStageBoardItemVm> StageBoard { get; init; } =
        Array.Empty<OngoingStageBoardItemVm>();

    // SECTION: Headline figures (computed once in the PageModel from the stage rows)
    /// <summary>Stage holding the most projects in the current selection, or null when empty.</summary>
    public string? TopStageName { get; init; }
    public int TopStageCount { get; init; }

    /// <summary>Median days the selected ongoing projects have spent in their current stage.</summary>
    public double? MedianDaysInCurrentStage { get; init; }

    /// <summary>Projects whose current stage has run longer than <see cref="LongStageThresholdDays"/>.</summary>
    public int ProjectsPastThreshold { get; init; }
    public const int LongStageThresholdDays = 180;

    /// <summary>The four workflow phases (Pre-AoN, Procurement, Development, Post-development) in order, with how many of the selected projects stand in each.</summary>
    public IReadOnlyList<OngoingPhaseVm> Phases { get; init; } = Array.Empty<OngoingPhaseVm>();
    // END SECTION
}

/// <summary>One phase card: the stages it spans (workflow order), its project count and how many of those have sat in one stage past the threshold.</summary>
public sealed record OngoingPhaseVm(
    string Key,
    string Name,
    string Span,
    IReadOnlyList<string> StageCodes,
    int Count,
    int PastThresholdCount);

public sealed record OngoingStageBoardItemVm(
    string StageCode,
    string StageName,
    int StageCount,
    IReadOnlyList<OngoingStageBoardCategoryVm> Categories);

public sealed record OngoingStageBoardCategoryVm(
    int? ParentCategoryId,
    string ParentCategoryName,
    int CategoryCount,
    IReadOnlyList<OngoingStageBoardProjectVm> Projects);

public sealed record OngoingStageBoardProjectVm(int ProjectId, string ProjectName);
// END SECTION
