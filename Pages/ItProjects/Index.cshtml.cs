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
/// The board of one module on the IT projects engine: where every case stands, stage by stage
/// (docs/ux/it-projects.md, "The board"). IT, MOLTI and SRE projects each have their own page
/// class and route (docs/ux/molti-sre-projects.md); this is everything they share, and
/// <see cref="ModuleCode"/> is the only thing that differs.
/// </summary>
public abstract class ItProjectBoardPageModel : PageModel
{
    private readonly IItProjectReadService _reads;

    protected ItProjectBoardPageModel(IItProjectReadService reads)
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
    }

    /// <summary>The module this page belongs to (<see cref="ItProjectModules"/>). Fixed by the page class, never by the request.</summary>
    public abstract string ModuleCode { get; }

    /// <summary>The module's names and wording.</summary>
    public ItProjectModules.Definition Module => ItProjectModules.Get(ModuleCode);

    /// <summary>active (default), completed, dropped or all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Scope { get; set; }

    /// <summary>Financial year start, e.g. 2026 for 2026-27. Empty means all years.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Fy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>"wait" sorts live cases by the longest wait; anything else keeps stage order.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>"missing" shows only the live cases with a completed stage that has no date.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Dates { get; set; }

    public ItProjectBoardSort BoardSort => string.Equals(Sort, "wait", StringComparison.OrdinalIgnoreCase)
        ? ItProjectBoardSort.LongestWait
        : ItProjectBoardSort.StageOrder;

    /// <summary>The route value of the current sort, so links keep it.</summary>
    public string? SortKey => BoardSort == ItProjectBoardSort.LongestWait ? "wait" : null;

    public bool OnlyMissingDates => string.Equals(Dates, "missing", StringComparison.OrdinalIgnoreCase);

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

        CanManage = ItProjectAccess.CanManage(User, ModuleCode);

        // One query for every scope: the tiles need all rows, and the scope only picks which
        // rows the board draws.
        Board = await _reads.GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Fy, Q, BoardSort, ModuleCode), cancellationToken);
        ScopedRows = OnlyMissingDates
            ? Board.Rows.Where(row => InScope(row.Status.Kind, ItProjectBoardScope.Active) && row.Timing.HasMissingDates).ToList()
            : Board.Rows.Where(row => InScope(row.Status.Kind, BoardScope)).ToList();
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

/// <summary>The IT projects board (<c>/ItProjects</c>).</summary>
[Authorize(Policy = Policies.ItProjects.View)]
public class IndexModel : ItProjectBoardPageModel
{
    public IndexModel(IItProjectReadService reads)
        : base(reads)
    {
    }

    public override string ModuleCode => ItProjectModules.It;
}
