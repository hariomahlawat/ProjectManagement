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

    public IndexModel(IItProjectReadService reads)
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
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
        Array.Empty<ItProjectBoardRow>(), Array.Empty<ItProjectBoardStageCount>(), 0, 0, 0, Array.Empty<int>(),
        new ItProjectBoardFigures(null, 0, 0, 0m, 0, 0, null, 0, 0, 0, 0, 0, 0));

    /// <summary>The rows the chosen scope draws, in the service's board order.</summary>
    public IReadOnlyList<ItProjectBoardRow> ScopedRows { get; private set; } = Array.Empty<ItProjectBoardRow>();

    public IReadOnlyList<ItProjectBoardPresentation.Band> Bands { get; private set; } = Array.Empty<ItProjectBoardPresentation.Band>();

    public bool CanManage { get; private set; }

    /// <summary>The current financial year's start (IST), resolved once in the read service.</summary>
    public int CurrentFinancialYearStart { get; private set; }

    /// <summary>How many years the header shows as buttons; older ones go in the "Earlier" list.</summary>
    public const int RecentYearButtons = 4;

    /// <summary>The newest years some case counts in, shown as buttons beside "All years".</summary>
    public IReadOnlyList<int> RecentYears { get; private set; } = Array.Empty<int>();

    /// <summary>Older years, offered in the "Earlier" list.</summary>
    public IReadOnlyList<int> EarlierYears { get; private set; } = Array.Empty<int>();

    public int AllCount => Board.ActiveCount + Board.CompletedCount + Board.DroppedCount;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        BoardScope = ParseScope(Scope);
        if (Fy is { } year && !CreateModel.IsSupportedFinancialYear(year))
        {
            // An unsupported year in the address (a typo, an old link) means "all years", not an error.
            Fy = null;
        }

        CanManage = ItProjectAccess.CanManage(User);

        // One query for every scope: the tiles need all rows, and the scope only picks which
        // rows the board draws.
        Board = await _reads.GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Fy, Q), cancellationToken);
        ScopedRows = Board.Rows.Where(row => InScope(row.Status.Kind, BoardScope)).ToList();
        Bands = ItProjectBoardPresentation.Bands(ScopedRows, GroupByFinancialYear);

        CurrentFinancialYearStart = Board.Figures.CurrentFinancialYearStart;
        RecentYears = Board.FinancialYears.Take(RecentYearButtons).ToList();
        EarlierYears = Board.FinancialYears.Skip(RecentYearButtons).ToList();

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
}
