using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    private const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    [Display(Name = "Search")]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<Row> Items { get; private set; } = Array.Empty<Row>();

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var query = _db.LineDirectorates.AsNoTracking();

        var statusFilter = (Status ?? "active").Trim().ToLowerInvariant();
        query = statusFilter switch
        {
            "inactive" => query.Where(l => !l.IsActive),
            "all" => query,
            _ => query.Where(l => l.IsActive)
        };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(l => EF.Functions.ILike(l.Name, $"%{term}%"));
        }

        query = query
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name);

        TotalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

        if (PageNumber < 1)
        {
            PageNumber = 1;
        }
        else if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Items = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(l => new Row
            {
                Id = l.Id,
                Name = l.Name,
                SortOrder = l.SortOrder,
                IsActive = l.IsActive,
                ProjectCount = l.Projects.Count
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int offset)
    {
        if (offset == 0)
        {
            StatusMessage = "No changes made.";
            return RedirectToPage(new { q = Q, status = Status, pageNumber = PageNumber });
        }

        var lineDirectorate = await _db.LineDirectorates.SingleOrDefaultAsync(l => l.Id == id);
        if (lineDirectorate is null)
        {
            return NotFound();
        }

        var siblings = await _db.LineDirectorates
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync();

        var index = siblings.FindIndex(l => l.Id == id);
        if (index < 0)
        {
            StatusMessage = "Unable to reorder line directorates.";
            return RedirectToPage(new { q = Q, status = Status, pageNumber = PageNumber });
        }

        var targetIndex = Math.Clamp(index + offset, 0, siblings.Count - 1);
        if (targetIndex == index)
        {
            StatusMessage = offset < 0
                ? $"'{lineDirectorate.Name}' is already at the top."
                : $"'{lineDirectorate.Name}' is already at the bottom.";
            return RedirectToPage(new { q = Q, status = Status, pageNumber = PageNumber });
        }

        var moving = siblings[index];
        siblings.RemoveAt(index);
        siblings.Insert(targetIndex, moving);

        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].SortOrder != i)
            {
                siblings[i].SortOrder = i;
            }
        }

        await _db.SaveChangesAsync();

        StatusMessage = offset < 0
            ? $"Moved '{lineDirectorate.Name}' up."
            : $"Moved '{lineDirectorate.Name}' down.";

        return RedirectToPage(new { q = Q, status = Status, pageNumber = PageNumber });
    }

    public sealed class Row
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public bool IsActive { get; init; }
        public int ProjectCount { get; init; }
    }
}
