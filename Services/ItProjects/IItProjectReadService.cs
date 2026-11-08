using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

public enum ItProjectBoardScope
{
    Active = 0,
    Completed = 1,
    Dropped = 2,
    All = 3
}

public sealed record ItProjectBoardQuery(
    ItProjectBoardScope Scope = ItProjectBoardScope.Active,
    int? FinancialYearStart = null,
    string? Search = null);

public sealed record ItProjectBoardStageCell(
    string StageCode,
    int SortOrder,
    bool IsCompleted,
    DateOnly? CompletedOn,
    bool IsSkipped,
    decimal? CostLakhs,
    bool IsCurrent);

public sealed record ItProjectBoardRow(
    int Id,
    string Title,
    string PppReferenceNumber,
    int FinancialYearStart,
    ItProjectStatus Status,
    DateOnly? DroppedOn,
    string? DroppedReason,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ItProjectBoardStageCell> Stages,
    ItProjectTiming Timing);

public sealed record ItProjectBoardStageCount(string StageCode, int AtStage);

/// <summary>The active case that has waited longest at its stage, by stage dates only.</summary>
public sealed record ItProjectLongestWait(int Id, string Title, string StageCode, int Days);

/// <summary>
/// Board-wide figures (docs/ux/it-projects-redesign.md). Like the scope counts they follow the
/// financial year and search but not the scope.
/// </summary>
public sealed record ItProjectBoardFigures(
    ItProjectLongestWait? LongestWait,
    int ActiveWithoutStageDate,
    int DatesMissingCount,
    decimal OnOrderLakhs,
    int OnOrderCount,
    int DeliveredThisFinancialYear,
    DateOnly? LastDeliveredOn);

public sealed record ItProjectBoard(
    IReadOnlyList<ItProjectBoardRow> Rows,
    IReadOnlyList<ItProjectBoardStageCount> StageCounts,
    int ActiveCount,
    int CompletedCount,
    int DroppedCount,
    IReadOnlyList<int> FinancialYears,
    ItProjectBoardFigures Figures);

public interface IItProjectReadService
{
    /// <summary>
    /// The board: rows in board order (active by current stage then title, then completed,
    /// then dropped), the "at stage" counts and the scope counts. The counts ignore the scope
    /// filter but honour the financial year and search, so the pills always add up to what the
    /// user is looking at.
    /// </summary>
    Task<ItProjectBoard> GetBoardAsync(ItProjectBoardQuery query, CancellationToken cancellationToken = default);

    /// <summary>The project with its nine stage rows in order, or null when it does not exist or is deleted.</summary>
    Task<ItProject?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The time facts of a project loaded by <see cref="GetAsync"/>, against today in IST.</summary>
    ItProjectTiming GetTiming(ItProject project);

    /// <summary>A user's display name (full name, else user name) for "updated by", or null when unknown.</summary>
    Task<string?> GetUserDisplayNameAsync(string userId, CancellationToken cancellationToken = default);
}
