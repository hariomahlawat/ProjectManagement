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

    public MapModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var rows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);
        return new JsonResult(rows);
    }
}
