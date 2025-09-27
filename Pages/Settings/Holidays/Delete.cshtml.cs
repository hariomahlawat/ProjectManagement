using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;

namespace ProjectManagement.Pages.Settings.Holidays;

[Authorize(Roles = "Admin,HoD")]
public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public DeleteModel(ApplicationDbContext db) => _db = db;

    public Holiday? Item { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Item = await _db.Holidays
            .AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == id, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var holiday = await _db.Holidays.FindAsync(new object[] { id }, cancellationToken);
        if (holiday is null)
        {
            return RedirectToPage("Index");
        }

        _db.Holidays.Remove(holiday);
        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("Index");
    }
}
