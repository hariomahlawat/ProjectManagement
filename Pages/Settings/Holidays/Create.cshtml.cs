using System.ComponentModel.DataAnnotations;
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
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public CreateModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var exists = await _db.Holidays
            .AsNoTracking()
            .AnyAsync(h => h.Date == Input.Date, cancellationToken);

        if (exists)
        {
            ModelState.AddModelError("Input.Date", "A holiday already exists for this date.");
            return Page();
        }

        var name = Input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("Input.Name", "Name is required.");
            return Page();
        }

        _db.Holidays.Add(new Holiday
        {
            Date = Input.Date,
            Name = name
        });

        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("Index");
    }

    public sealed class InputModel
    {
        [Required]
        [DataType(DataType.Date)]
        public DateOnly Date { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;
    }
}
