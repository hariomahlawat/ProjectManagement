using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;

namespace ProjectManagement.Pages.Settings.Holidays;

[Authorize(Roles = "Admin,HoD")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public List<Holiday> Items { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _db.Holidays
            .AsNoTracking()
            .OrderBy(h => h.Date)
            .ToListAsync(cancellationToken);
    }
}
