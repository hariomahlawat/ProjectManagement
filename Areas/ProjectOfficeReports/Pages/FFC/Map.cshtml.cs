using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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
        var results = await _db.FfcCountries
            .AsNoTracking()
            .Select(country => new
            {
                countryId = country.Id,
                iso3 = country.IsoCode,
                name = country.Name,
                delivered = country.Records
                    .Where(record => !record.IsDeleted && record.DeliveryYes)
                    .Count(),
                perYear = country.Records
                    .Where(record => !record.IsDeleted && record.DeliveryYes)
                    .GroupBy(record => record.Year)
                    .Select(group => new
                    {
                        year = group.Key,
                        count = group.Count()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return new JsonResult(results);
    }
}
