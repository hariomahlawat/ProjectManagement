using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public IndexModel(ApplicationDbContext db, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
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

    [BindProperty]
    public CreateInputModel CreateInput { get; set; } = new();

    [BindProperty]
    public EditInputModel EditInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? OpenModal { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    public bool ShouldOpenCreateModal { get; private set; }

    public bool ShouldOpenEditModal { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadPageAsync();

        if (string.Equals(OpenModal, "create", StringComparison.OrdinalIgnoreCase))
        {
            ShouldOpenCreateModal = true;
        }
        else if (string.Equals(OpenModal, "edit", StringComparison.OrdinalIgnoreCase) && EditId.HasValue)
        {
            if (!await TryPopulateEditInputAsync(EditId.Value))
            {
                return NotFound();
            }

            ShouldOpenEditModal = true;
        }

        return Page();
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

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var trimmedName = CreateInput.Name?.Trim();
        if (trimmedName is not null)
        {
            CreateInput.Name = trimmedName;
        }

        if (!ModelState.IsValid)
        {
            ShouldOpenCreateModal = true;
            await LoadPageAsync();
            return Page();
        }

        var exists = await _db.LineDirectorates.AnyAsync(l => l.Name == CreateInput.Name);
        if (exists)
        {
            ModelState.AddModelError("CreateInput.Name", "A line directorate with this name already exists.");
            ShouldOpenCreateModal = true;
            await LoadPageAsync();
            return Page();
        }

        var now = DateTime.UtcNow;
        var entity = new ProjectManagement.Models.LineDirectorate
        {
            Name = CreateInput.Name,
            SortOrder = CreateInput.SortOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _db.LineDirectorates.Add(entity);
        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.LineDirectorateCreated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["LineDirectorateId"] = entity.Id.ToString(),
                ["Name"] = entity.Name,
                ["SortOrder"] = entity.SortOrder.ToString()
            });

        StatusMessage = $"Created '{entity.Name}'.";
        return RedirectToPage(new { q = Q, status = Status, pageNumber = Math.Max(1, PageNumber) });
    }

    public async Task<IActionResult> OnPostEditAsync()
    {
        var trimmedName = EditInput.Name?.Trim();
        if (trimmedName is not null)
        {
            EditInput.Name = trimmedName;
        }

        if (!ModelState.IsValid)
        {
            ShouldOpenEditModal = true;
            await LoadPageAsync();
            return Page();
        }

        var entity = await _db.LineDirectorates.FindAsync(EditInput.Id);
        if (entity is null)
        {
            return NotFound();
        }

        var duplicate = await _db.LineDirectorates
            .AnyAsync(l => l.Id != EditInput.Id && l.Name == EditInput.Name);

        if (duplicate)
        {
            ModelState.AddModelError("EditInput.Name", "A line directorate with this name already exists.");
            ShouldOpenEditModal = true;
            await LoadPageAsync();
            return Page();
        }

        var originalName = entity.Name;
        var originalSortOrder = entity.SortOrder;

        entity.Name = EditInput.Name;
        entity.SortOrder = EditInput.SortOrder;
        entity.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.LineDirectorateUpdated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["LineDirectorateId"] = entity.Id.ToString(),
                ["NameBefore"] = originalName,
                ["NameAfter"] = entity.Name,
                ["SortOrderBefore"] = originalSortOrder.ToString(),
                ["SortOrderAfter"] = entity.SortOrder.ToString()
            });

        StatusMessage = $"Updated '{entity.Name}'.";
        return RedirectToPage(new { q = Q, status = Status, pageNumber = Math.Max(1, PageNumber) });
    }

    public sealed class Row
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public bool IsActive { get; init; }
        public int ProjectCount { get; init; }
    }

    public abstract class FormInputModel
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 1000)]
        [Display(Name = "Sort Order")]
        public int SortOrder { get; set; }
    }

    public sealed class CreateInputModel : FormInputModel
    {
    }

    public sealed class EditInputModel : FormInputModel
    {
        [HiddenInput]
        public int Id { get; set; }
    }

    private async Task LoadPageAsync()
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

    private async Task<bool> TryPopulateEditInputAsync(int id)
    {
        var entity = await _db.LineDirectorates.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id);
        if (entity is null)
        {
            return false;
        }

        EditInput = new EditInputModel
        {
            Id = entity.Id,
            Name = entity.Name,
            SortOrder = entity.SortOrder
        };

        return true;
    }
}
