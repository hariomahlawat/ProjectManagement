using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class MapModel : PageModel
{
    private readonly ApplicationDbContext _db;

    // SECTION: Summary counters
    public int TotalCountriesWithUnits { get; private set; }

    public int TotalUnits { get; private set; }

    public int TotalInstalled { get; private set; }

    public int TotalDelivered { get; private set; }

    public int TotalPlanned { get; private set; }

    public MapModel(ApplicationDbContext db)
    {
        _db = db;
    }

    // SECTION: Request handlers
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("World map", null));

        var rows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);

        TotalCountriesWithUnits = rows.Count(row => (row?.Total ?? 0) > 0);
        TotalInstalled = rows.Sum(row => row?.Installed ?? 0);
        TotalDelivered = rows.Sum(row => row?.Delivered ?? 0);
        TotalPlanned = rows.Sum(row => row?.Planned ?? 0);
        TotalUnits = TotalInstalled + TotalDelivered + TotalPlanned;
    }

    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var rows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);
        return new JsonResult(rows);
    }
}
