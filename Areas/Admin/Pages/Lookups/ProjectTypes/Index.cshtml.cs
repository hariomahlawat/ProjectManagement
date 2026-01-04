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

namespace ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes;

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
        // SECTION: Filtered lookup query
        var query = _db.ProjectTypes
            .AsNoTracking();

        var statusFilter = (Status ?? "active").Trim().ToLowerInvariant();
        query = statusFilter switch
        {
            "inactive" => query.Where(u => !u.IsActive),
            "all" => query,
            _ => query.Where(u => u.IsActive)
        };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(u => EF.Functions.ILike(u.Name, $"%{term}%"));
        }

        query = query
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Name);

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
            .Select(u => new Row
            {
                Id = u.Id,
                Name = u.Name,
                SortOrder = u.SortOrder,
                IsActive = u.IsActive,
                ProjectCount = u.Projects.Count
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int offset)
    {
        if (offset == 0)
        {
            StatusMessage = "No changes made.";
            return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
        }

        // SECTION: Reorder project types
        var types = await _db.ProjectTypes
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Name)
            .ToListAsync();

        var currentIndex = types.FindIndex(u => u.Id == id);
        if (currentIndex < 0)
        {
            return NotFound();
        }

        var targetIndex = Math.Clamp(currentIndex + offset, 0, types.Count - 1);
        if (targetIndex == currentIndex)
        {
            StatusMessage = offset < 0 ? "Already at the top." : "Already at the bottom.";
            return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
        }

        var projectType = types[currentIndex];
        types.RemoveAt(currentIndex);
        types.Insert(targetIndex, projectType);

        var anyChanges = false;
        for (var i = 0; i < types.Count; i++)
        {
            var desiredOrder = i + 1;
            if (types[i].SortOrder != desiredOrder)
            {
                types[i].SortOrder = desiredOrder;
                anyChanges = true;
            }
        }

        if (anyChanges)
        {
            await _db.SaveChangesAsync();
            StatusMessage = offset < 0
                ? $"Moved '{projectType.Name}' up."
                : $"Moved '{projectType.Name}' down.";
        }
        else
        {
            StatusMessage = "No changes made.";
        }

        return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
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
