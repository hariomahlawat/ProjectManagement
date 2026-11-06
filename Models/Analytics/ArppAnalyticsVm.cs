using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Models.Analytics;

// SECTION: ARPP analytics view model (Analytics → ARPP mode)
/// <summary>
/// The projects listed in one financial year's published ARPP (current position: the latest
/// published row per project), joined to where each project stands today. Every figure is
/// computed once in the PageModel; views only read.
/// </summary>
public sealed class ArppAnalyticsVm
{
    public int FinancialYearStart { get; init; }
    public string FinancialYearDisplay { get; init; } = string.Empty;
    public IReadOnlyList<int> AvailableFinancialYears { get; init; } = Array.Empty<int>();

    /// <summary>True when the year shown is the financial year of the "as of" date.</summary>
    public bool IsCurrentFinancialYear { get; init; }
    /// <summary>
    /// Set for a past year: the page shows where its projects stood on this date, the last day of that
    /// financial year. Null: today.
    /// </summary>
    public DateOnly? AsOfDate { get; init; }
    /// <summary>
    /// Set for a past year (its 31 March), whichever view is shown: the page then offers the switch between
    /// that date and today.
    /// </summary>
    public DateOnly? YearEndDate { get; init; }
    /// <summary>A past year shown as of today (its Today switch).</summary>
    public bool ShowsToday => YearEndDate.HasValue && AsOfDate is null;
    /// <summary>Set when the current financial year has no published ARPP and an earlier year is shown instead.</summary>
    public string? FallbackNote { get; init; }

    public bool HasData => ListedCount > 0 || DelistedCount > 0;

    // Listing
    public int ListedCount { get; init; }
    public decimal ListedIpaValue { get; init; }
    public int NewCount { get; init; }
    public int CommittedLiabilityCount { get; init; }
    public int CarryForwardCount { get; init; }
    public int DelistedCount { get; init; }
    public decimal DelistedIpaValue { get; init; }
    public int AddedByAddendumCount { get; init; }
    public int UnlinkedRowCount { get; init; }

    // Where the listed projects stand
    public int OngoingCount { get; init; }
    public int CompletedCount { get; init; }
    public int CancelledCount { get; init; }
    /// <summary>Projects whose lifecycle on the date cannot be told (completed or cancelled with only a year).</summary>
    public int NotKnownCount { get; init; }

    /// <summary>Ongoing listed projects by current stage, every workflow stage in order.</summary>
    public IReadOnlyList<ArppStageCountVm> ByStage { get; init; } = Array.Empty<ArppStageCountVm>();

    /// <summary>Ongoing listed projects by stage and parent category (only categories present): the stacked stage chart.</summary>
    public IReadOnlyList<OngoingStageByParentCategoryPoint> ByStageByCategory { get; init; } = Array.Empty<OngoingStageByParentCategoryPoint>();

    /// <summary>The categories in the stage chart, in their own order: the legend and the row chips share it.</summary>
    public IReadOnlyList<string> StageCategoryOrder { get; init; } = Array.Empty<string>();

    /// <summary>One line per listed project for the progress slides, in the same buckets as the cards.</summary>
    public IReadOnlyList<ArppProjectLineVm> ProjectLines { get; init; } = Array.Empty<ArppProjectLineVm>();

    /// <summary>For each standing row (by its key): its parent categories, in order, with their projects.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ArppStandingCategoryVm>> StandingCategories { get; init; } =
        new Dictionary<string, IReadOnlyList<ArppStandingCategoryVm>>();

    /// <summary>Completed, then each stage with projects, then cancelled: the in-page drill-down.</summary>
    public IReadOnlyList<AnalyticsBoardGroupVm> StandingBoard { get; init; } = Array.Empty<AnalyticsBoardGroupVm>();

    // Composition
    public IReadOnlyList<ArppValueGroupVm> ByCategory { get; init; } = Array.Empty<ArppValueGroupVm>();
    public IReadOnlyList<ArppValueGroupVm> ByCfa { get; init; } = Array.Empty<ArppValueGroupVm>();
    public IReadOnlyList<ArppValueGroupVm> ByFund { get; init; } = Array.Empty<ArppValueGroupVm>();
    public IReadOnlyList<ArppValueGroupVm> ByDfpds { get; init; } = Array.Empty<ArppValueGroupVm>();
    public IReadOnlyList<ArppValueGroupVm> ByCostBand { get; init; } = Array.Empty<ArppValueGroupVm>();
    public IReadOnlyList<ArppValueGroupVm> ByParentCategory { get; init; } = Array.Empty<ArppValueGroupVm>();

    // Milestones (approved listed projects; the supply-order figures count projects still in work)
    /// <summary>Listed projects whose AoN stage is completed (dated or not) or skipped.</summary>
    public int PastAonCount { get; init; }
    /// <summary>Ongoing listed projects with a supply order placed (a supply-order date, or a stage beyond Supply Order).</summary>
    public int SupplyOrderCount { get; init; }
    /// <summary>Their recorded supply-order value.</summary>
    public decimal SupplyOrderValue { get; init; }
    /// <summary>The recorded supply-order value of the completed listed projects, reported on its own.</summary>
    public decimal CompletedSupplyOrderValue { get; init; }
    public decimal TotalSupplyOrderValue => SupplyOrderValue + CompletedSupplyOrderValue;
    /// <summary>Ongoing listed projects that have not reached a supply order (cancelled projects are in no card).</summary>
    public int BeforeSupplyOrderCount { get; init; }
    /// <summary>Of those, the ones past AoN: the "AoN to SO stage" card.</summary>
    public int AonToSupplyOrderCount { get; init; }
    /// <summary>Of those, the ones not yet past AoN (FS, IPA, SoW, or the AoN stage itself): the "Pre-AoN stage" card.</summary>
    public int PreAonCount { get; init; }
    /// <summary>On a past date: projects the records cannot place in one card. Zero for today.</summary>
    public int CardNotKnownCount { get; init; }
    public IReadOnlyList<AnalyticsBoardGroupVm> MilestoneBoard { get; init; } = Array.Empty<AnalyticsBoardGroupVm>();
}

public sealed record ArppStageCountVm(string StageCode, string StageName, int Count);

/// <summary>Where a listed project sits on the progress slides (the same split as the headline cards).</summary>
public enum ArppProgressBucket
{
    PreAon,
    AonToSupplyOrder,
    UnderDevelopment,
    Completed,
    Cancelled,
    NotKnown
}

/// <summary>
/// One listed project as the progress slides show it: its parent category, bucket, stage on the view's date
/// (code, name and its place in the workflow), development PDC (only at the Development stage) and whether
/// it is past, and for a completed project its completion month and year.
/// </summary>
public sealed record ArppProjectLineVm(
    int ProjectId,
    string ProjectName,
    string Category,
    ArppProgressBucket Bucket,
    string? StageCode,
    string? StageName,
    int StageRank,
    DateOnly? DevelopmentPdc,
    bool PdcOverdue,
    string? CompletedLabel,
    DateOnly? CompletedSort);

/// <summary>One parent category within a standing row: its count and its projects.</summary>
public sealed record ArppStandingCategoryVm(string Name, int Count, IReadOnlyList<AnalyticsBoardProjectVm> Projects);

/// <summary>A composition group: count and IPA value of the listed projects in it, with the projects for the drill-down.</summary>
public sealed record ArppValueGroupVm(
    string Key,
    string Name,
    int Count,
    decimal IpaValue,
    IReadOnlyList<AnalyticsBoardProjectVm> Projects);
// END SECTION

// SECTION: Shared drill-down board DTOs (group → projects, opened in place from a chart)
public sealed record AnalyticsBoardGroupVm(
    string Key,
    string Name,
    int Count,
    IReadOnlyList<AnalyticsBoardProjectVm> Projects,
    string? Hint = null);

public sealed record AnalyticsBoardProjectVm(
    int ProjectId,
    string ProjectName,
    string? Detail = null);
// END SECTION
