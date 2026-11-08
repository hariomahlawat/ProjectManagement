using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.ItProjects;

/// <summary>
/// The IT projects board: where every IT PPP case stands, stage by stage
/// (docs/ux/it-projects.md, "The board").
/// </summary>
[Authorize(Policy = Policies.ItProjects.View)]
public class IndexModel : PageModel
{
    private readonly IItProjectReadService _reads;
    private readonly IClock _clock;

    public IndexModel(IItProjectReadService reads, IClock clock)
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>active (default), completed, dropped or all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Scope { get; set; }

    /// <summary>Financial year start, e.g. 2026 for 2026-27. Empty means all years.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Fy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>"fy" groups the board by financial year.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Group { get; set; }

    public ItProjectBoardScope BoardScope { get; private set; } = ItProjectBoardScope.Active;

    public bool GroupByFinancialYear => string.Equals(Group, "fy", StringComparison.OrdinalIgnoreCase);

    public ItProjectBoard Board { get; private set; } = new(
        Array.Empty<ItProjectBoardRow>(), Array.Empty<ItProjectBoardStageCount>(), 0, 0, 0, Array.Empty<int>());

    /// <summary>The rows the chosen scope draws, in the service's board order.</summary>
    public IReadOnlyList<ItProjectBoardRow> ScopedRows { get; private set; } = Array.Empty<ItProjectBoardRow>();

    public IReadOnlyList<ItProjectBoardPresentation.Band> Bands { get; private set; } = Array.Empty<ItProjectBoardPresentation.Band>();

    public bool CanManage { get; private set; }

    /// <summary>The current financial year's start, for "Completed this FY".</summary>
    public int CurrentFinancialYearStart { get; private set; }

    public int CompletedThisFinancialYear { get; private set; }

    /// <summary>The stage with the most active projects at it, or null when the board is empty.</summary>
    public ItProjectBoardStageCount? BusiestStage { get; private set; }

    public int AllCount => Board.ActiveCount + Board.CompletedCount + Board.DroppedCount;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        BoardScope = ParseScope(Scope);
        CanManage = ItProjectAccess.CanManage(User);
        CurrentFinancialYearStart = FinancialYearHelper.GetStartYear(DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

        // One query for every scope: the tiles need all rows, and the scope only picks which
        // rows the board draws.
        Board = await _reads.GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Fy, Q), cancellationToken);
        ScopedRows = Board.Rows.Where(row => InScope(row.Status.Kind, BoardScope)).ToList();
        Bands = ItProjectBoardPresentation.Bands(ScopedRows, GroupByFinancialYear);

        // "Completed this FY" counts by the delivery date when it is known, else by the project's FY.
        CompletedThisFinancialYear = Board.Rows.Count(row =>
            row.Status.Kind == ItProjectStatusKind.Completed &&
            CompletedInFinancialYear(row, CurrentFinancialYearStart));

        BusiestStage = Board.StageCounts
            .Where(count => count.AtStage > 0)
            .OrderByDescending(count => count.AtStage)
            .ThenBy(count => ItProjectStages.OrderOf(count.StageCode))
            .FirstOrDefault();

        return Page();
    }

    public string ScopeKey => BoardScope switch
    {
        ItProjectBoardScope.Completed => "completed",
        ItProjectBoardScope.Dropped => "dropped",
        ItProjectBoardScope.All => "all",
        _ => "active"
    };

    public static ItProjectBoardScope ParseScope(string? scope) => (scope ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "completed" => ItProjectBoardScope.Completed,
        "dropped" => ItProjectBoardScope.Dropped,
        "all" => ItProjectBoardScope.All,
        _ => ItProjectBoardScope.Active
    };

    public static bool InScope(ItProjectStatusKind kind, ItProjectBoardScope scope) => scope switch
    {
        ItProjectBoardScope.Active => kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress,
        ItProjectBoardScope.Completed => kind == ItProjectStatusKind.Completed,
        ItProjectBoardScope.Dropped => kind == ItProjectStatusKind.Dropped,
        _ => true
    };

    private static bool CompletedInFinancialYear(ItProjectBoardRow row, int financialYearStart)
    {
        var delivery = row.Stages.FirstOrDefault(cell => cell.StageCode == ItProjectStages.Delivery);
        return delivery?.CompletedOn is { } date
            ? FinancialYearHelper.Contains(financialYearStart, date)
            : row.FinancialYearStart == financialYearStart;
    }
}
