using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Projects;

[Authorize(Roles = "Admin")]
public class LegacyImportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectImportService _importService;

    public LegacyImportModel(ApplicationDbContext db, IProjectImportService importService)
    {
        _db = db;
        _importService = importService;
    }

    [BindProperty(SupportsGet = true)]
    public int? ProjectCategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? TechnicalCategoryId { get; set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public IReadOnlyList<SelectListItem> ProjectCategories { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> TechnicalCategories { get; private set; } = Array.Empty<SelectListItem>();

    public async Task OnGetAsync()
    {
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        await LoadOptionsAsync();

        if (!ProjectCategoryId.HasValue)
        {
            ModelState.AddModelError(nameof(ProjectCategoryId), "Select a project category.");
        }

        if (!TechnicalCategoryId.HasValue)
        {
            ModelState.AddModelError(nameof(TechnicalCategoryId), "Select a technical category.");
        }

        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), "Upload a non-empty .xlsx file.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var importerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "legacy-import";
        var result = await _importService.ImportLegacyProjectsAsync(
            ProjectCategoryId!.Value,
            TechnicalCategoryId!.Value,
            Upload!,
            importerUserId);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Import failed.");
            return Page();
        }

        StatusMessage = $"Imported {result.RowsImported} projects for the selected categories.";
        return RedirectToPage(new
        {
            ProjectCategoryId,
            TechnicalCategoryId
        });
    }

    public IActionResult OnGetTemplate()
    {
        var workbook = ProjectLegacyImportTemplateFactory.CreateWorkbook();
        using var stream = new System.IO.MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "LegacyProjectsTemplate.xlsx");
    }

    private async Task LoadOptionsAsync()
    {
        ProjectCategories = await _db.ProjectCategories
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem
            {
                Value = x.Id.ToString(CultureInfo.InvariantCulture),
                Text = x.Name,
                Selected = ProjectCategoryId.HasValue && ProjectCategoryId.Value == x.Id
            })
            .ToListAsync();

        TechnicalCategories = await _db.TechnicalCategories
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem
            {
                Value = x.Id.ToString(CultureInfo.InvariantCulture),
                Text = x.Name,
                Selected = TechnicalCategoryId.HasValue && TechnicalCategoryId.Value == x.Id
            })
            .ToListAsync();
    }
}
