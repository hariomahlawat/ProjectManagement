using System;
using System.Collections.Generic;
using System.Linq;
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

public enum ItProjectBoardSort
{
    /// <summary>Live cases by current stage, then title (the approved default).</summary>
    StageOrder = 0,

    /// <summary>Live cases by days at the current stage from stage dates, longest first; cases without a stage date after them.</summary>
    LongestWait = 1
}

/// <summary>Which projects a board shows by their assigned Project Officer (modules that assign one).</summary>
public enum ItProjectOfficerFilter
{
    Any = 0,

    /// <summary>Projects with no officer: the ones waiting to be assigned.</summary>
    Unassigned = 1,

    /// <summary>Projects assigned to the viewer.</summary>
    Mine = 2
}

public sealed record ItProjectBoardQuery(
    ItProjectBoardScope Scope = ItProjectBoardScope.Active,
    /// <summary>Show the cases that count in this year (New ones and those carried into it); null for all years.</summary>
    int? FinancialYearStart = null,
    string? Search = null,
    ItProjectBoardSort Sort = ItProjectBoardSort.StageOrder,
    /// <summary>The module whose board this is; a board never shows the projects of another module.</summary>
    string Module = ItProjectModules.It,
    /// <summary>Narrows the board by assigned officer. Like the year and search, the counts follow it.</summary>
    ItProjectOfficerFilter Officer = ItProjectOfficerFilter.Any,
    /// <summary>The signed-in user, for <see cref="ItProjectOfficerFilter.Mine"/> and the "mine" count.</summary>
    string? ViewerUserId = null);

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
    ItProjectTiming Timing,
    ItProjectYearTypeInfo? YearType,
    IReadOnlyList<string> LaterYearReferences,
    /// <summary>The assigned Project Officer, in a module that assigns one; null when unassigned.</summary>
    string? ProjectOfficerUserId = null,
    /// <summary>The officer's display name; null when unassigned or the user no longer exists.</summary>
    string? ProjectOfficerName = null)
{
    /// <summary>The IT ARPPP number to show for the board's year: that year's number when entered, else the first one.</summary>
    public string DisplayReference => YearType?.PppReferenceNumber ?? PppReferenceNumber;

    /// <summary>
    /// What the board's instant search matches, lower case: the title, the first IT ARPPP number and
    /// every later year's number, the same fields the server search uses.
    /// </summary>
    public string SearchText => string.Join(' ', new[] { Title, PppReferenceNumber }.Concat(LaterYearReferences)).ToLowerInvariant();
}

public sealed record ItProjectBoardStageCount(string StageCode, int AtStage);

/// <summary>The active case that has waited longest at its stage, by stage dates only.</summary>
public sealed record ItProjectLongestWait(int Id, string Title, string StageCode, int Days);

/// <summary>
/// Board-wide figures (docs/ux/it-projects-redesign.md). Like the scope counts they follow the
/// financial year and search but not the scope. <see cref="FinancialYearStart"/> is the board's
/// year (the chosen one, else the current one): delivered and New / CL / CF counts are for it.
/// </summary>
public sealed record ItProjectBoardFigures(
    ItProjectLongestWait? LongestWait,
    int ActiveWithoutStageDate,
    int DatesMissingCount,
    decimal OnOrderLakhs,
    int OnOrderCount,
    int DeliveredInFinancialYear,
    DateOnly? LastDeliveredOn,
    int CurrentFinancialYearStart,
    int FinancialYearStart,
    int ActiveNew,
    int ActiveCommittedLiability,
    int ActiveCarriedForward,
    int NotStartedCount,
    int OnOrderWithoutValue = 0);

public sealed record ItProjectBoard(
    IReadOnlyList<ItProjectBoardRow> Rows,
    IReadOnlyList<ItProjectBoardStageCount> StageCounts,
    int ActiveCount,
    int CompletedCount,
    int DroppedCount,
    IReadOnlyList<int> FinancialYears,
    ItProjectBoardFigures Figures,
    /// <summary>Live projects with no officer, in the board's year and search but before the officer filter.</summary>
    int UnassignedActiveCount = 0,
    /// <summary>Live projects assigned to the viewer, counted the same way.</summary>
    int MineActiveCount = 0);

/// <summary>A user who can be a project's officer.</summary>
public sealed record ItProjectOfficerOption(string UserId, string Name);

public interface IItProjectReadService
{
    /// <summary>
    /// The board: rows in board order (active by current stage then title, then completed,
    /// then dropped), the "at stage" counts and the scope counts. The counts ignore the scope
    /// filter but honour the financial year and search, so the pills always add up to what the
    /// user is looking at.
    /// </summary>
    Task<ItProjectBoard> GetBoardAsync(ItProjectBoardQuery query, CancellationToken cancellationToken = default);

    /// <summary>The IT project with its nine stage rows in order, or null when it does not exist or is deleted.</summary>
    Task<ItProject?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The same for a project of the given module; a project of another module is null.</summary>
    Task<ItProject?> GetAsync(int id, string module, CancellationToken cancellationToken = default);

    /// <summary>The time facts of a project loaded by <see cref="GetAsync"/>, against today in IST.</summary>
    ItProjectTiming GetTiming(ItProject project);

    /// <summary>New / CL / CF in every financial year the project counts in, oldest first.</summary>
    IReadOnlyList<ItProjectYearTypeInfo> GetYearTypes(ItProject project);

    /// <summary>The active users who hold the Project Officer role, by name: the people a project can be assigned to.</summary>
    Task<IReadOnlyList<ItProjectOfficerOption>> GetAssignableOfficersAsync(CancellationToken cancellationToken = default);

    /// <summary>A user's display name (full name, else user name) for "updated by", or null when unknown.</summary>
    Task<string?> GetUserDisplayNameAsync(string userId, CancellationToken cancellationToken = default);
}
