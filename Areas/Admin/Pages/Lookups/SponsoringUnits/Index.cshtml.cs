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

namespace ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits;

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
            return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
        }

        var units = await _db.SponsoringUnits
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Name)
            .ToListAsync();

        var currentIndex = units.FindIndex(u => u.Id == id);
        if (currentIndex < 0)
        {
            return NotFound();
        }

        var targetIndex = Math.Clamp(currentIndex + offset, 0, units.Count - 1);
        if (targetIndex == currentIndex)
        {
            StatusMessage = offset < 0 ? "Already at the top." : "Already at the bottom.";
            return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
        }

        var unit = units[currentIndex];
        units.RemoveAt(currentIndex);
        units.Insert(targetIndex, unit);

        var anyChanges = false;
        for (var i = 0; i < units.Count; i++)
        {
            var desiredOrder = i + 1;
            if (units[i].SortOrder != desiredOrder)
            {
                units[i].SortOrder = desiredOrder;
                anyChanges = true;
            }
        }

        if (anyChanges)
        {
            await _db.SaveChangesAsync();
            StatusMessage = offset < 0
                ? $"Moved '{unit.Name}' up."
                : $"Moved '{unit.Name}' down.";
        }
        else
        {
            StatusMessage = "No changes made.";
        }

        return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
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

        var exists = await _db.SponsoringUnits
            .AnyAsync(u => u.Name == CreateInput.Name);

        if (exists)
        {
            ModelState.AddModelError("CreateInput.Name", "A sponsoring unit with this name already exists.");
            ShouldOpenCreateModal = true;
            await LoadPageAsync();
            return Page();
        }

        var now = DateTime.UtcNow;
        var unit = new ProjectManagement.Models.SponsoringUnit
        {
            Name = CreateInput.Name,
            SortOrder = CreateInput.SortOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _db.SponsoringUnits.Add(unit);
        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.SponsoringUnitCreated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["SponsoringUnitId"] = unit.Id.ToString(),
                ["Name"] = unit.Name,
                ["SortOrder"] = unit.SortOrder.ToString()
            });

        StatusMessage = $"Created '{unit.Name}'.";
        return RedirectToPage(new { pageNumber = Math.Max(1, PageNumber), q = Q, status = Status });
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

        var unit = await _db.SponsoringUnits.FindAsync(EditInput.Id);
        if (unit is null)
        {
            return NotFound();
        }

        var duplicate = await _db.SponsoringUnits
            .AnyAsync(u => u.Id != EditInput.Id && u.Name == EditInput.Name);

        if (duplicate)
        {
            ModelState.AddModelError("EditInput.Name", "A sponsoring unit with this name already exists.");
            ShouldOpenEditModal = true;
            await LoadPageAsync();
            return Page();
        }

        var originalName = unit.Name;
        var originalSort = unit.SortOrder;

        unit.Name = EditInput.Name;
        unit.SortOrder = EditInput.SortOrder;
        unit.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.SponsoringUnitUpdated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["SponsoringUnitId"] = unit.Id.ToString(),
                ["NameBefore"] = originalName,
                ["NameAfter"] = unit.Name,
                ["SortOrderBefore"] = originalSort.ToString(),
                ["SortOrderAfter"] = unit.SortOrder.ToString()
            });

        StatusMessage = $"Updated '{unit.Name}'.";
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
        var query = _db.SponsoringUnits.AsNoTracking();

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

    private async Task<bool> TryPopulateEditInputAsync(int id)
    {
        var unit = await _db.SponsoringUnits.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (unit is null)
        {
            return false;
        }

        EditInput = new EditInputModel
        {
            Id = unit.Id,
            Name = unit.Name,
            SortOrder = unit.SortOrder
        };

        return true;
    }
}
