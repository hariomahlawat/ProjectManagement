using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public sealed class MapModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public MapModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IActionResult OnGet(
        short? year = null,
        long? countryId = null,
        string? q = null,
        string? metric = null)
        => RedirectToPage(
            "/FFC/Footprint",
            new
            {
                area = "ProjectOfficeReports",
                view = "map",
                year,
                countryId,
                q,
                metric
            });

    // Retained for one compatibility cycle for older clients that requested the former map data endpoint.
    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var rows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);
        return new JsonResult(rows);
    }
}
