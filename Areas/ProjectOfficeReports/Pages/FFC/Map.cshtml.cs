using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class MapModel : PageModel
{
    private readonly ApplicationDbContext _db;

    private sealed class CountryProjectAgg
    {
        public long CountryId { get; init; }
        public string Iso3 { get; init; } = string.Empty;
        public int Installed { get; init; }
        public int Delivered { get; init; }
        public int Planned { get; init; }
        public int Total => Installed + Delivered + Planned;
    }

    public MapModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var linked = await _db.FfcProjects
            .AsNoTracking()
            .Where(project => project.LinkedProjectId != null)
            .Where(project =>
                !project.Record.IsDeleted &&
                project.Record.Country.IsActive &&
                project.LinkedProject != null &&
                !project.LinkedProject.IsDeleted &&
                project.LinkedProject.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(project => new
            {
                ProjectId = project.LinkedProjectId!.Value,
                project.Record.CountryId,
                CountryIso3 = project.Record.Country.IsoCode,
                Installed = project.Record.InstallationYes,
                Delivered = project.Record.DeliveryYes
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var aggregates = linked
            .GroupBy(item => new { item.CountryId, item.CountryIso3 })
            .Select(group => new CountryProjectAgg
            {
                CountryId = group.Key.CountryId,
                Iso3 = group.Key.CountryIso3 ?? string.Empty,
                Installed = group.Count(record => record.Installed),
                Delivered = group.Count(record => !record.Installed && record.Delivered),
                Planned = group.Count(record => !record.Installed && !record.Delivered)
            })
            .ToList();

        return new JsonResult(aggregates);
    }
}
