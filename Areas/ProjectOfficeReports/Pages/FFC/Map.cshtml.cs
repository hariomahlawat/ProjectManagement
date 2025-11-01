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
        var deliveryAggregates = await _db.FfcRecords
            .AsNoTracking()
            .Where(record => !record.IsDeleted && record.DeliveryYes)
            .GroupBy(record => new { record.CountryId, record.Year })
            .Select(group => new
            {
                group.Key.CountryId,
                group.Key.Year,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var countries = await _db.FfcCountries
            .AsNoTracking()
            .Where(country => country.IsActive)
            .Select(country => new
            {
                country.Id,
                country.IsoCode,
                country.Name
            })
            .ToListAsync(cancellationToken);

        var results = countries
            .Select(country =>
            {
                var aggregates = deliveryAggregates
                    .Where(aggregate => aggregate.CountryId == country.Id)
                    .ToList();

                var totalDelivered = aggregates.Sum(aggregate => aggregate.Count);

                return new
                {
                    countryId = country.Id,
                    iso3 = country.IsoCode,
                    name = country.Name,
                    delivered = totalDelivered,
                    perYear = aggregates
                        .GroupBy(aggregate => aggregate.Year)
                        .Select(group => new
                        {
                            year = group.Key,
                            count = group.Sum(item => item.Count)
                        })
                        .OrderBy(item => item.year)
                        .ToList()
                };
            })
            .Where(country => country.delivered > 0)
            .OrderByDescending(country => country.delivered)
            .ToList();

        return new JsonResult(results);
    }
}
