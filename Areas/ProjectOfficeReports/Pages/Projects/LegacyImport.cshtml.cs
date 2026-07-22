using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Maintenance;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Projects;

[Authorize(Policy = AdminPolicies.IngestionManage)]
public sealed class LegacyImportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ILegacyImportPreflightService _preflight;
    private readonly IAdminNavigationUrlBuilder _navigation;

    public LegacyImportModel(
        ApplicationDbContext db,
        ILegacyImportPreflightService preflight,
        IAdminNavigationUrlBuilder navigation)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    [BindProperty(SupportsGet = true)] public int? ProjectCategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public int? TechnicalCategoryId { get; set; }
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public Guid PreviewToken { get; set; }
    [BindProperty] public bool ConfirmCommit { get; set; }

    public AdminPageHeaderModel Header { get; private set; } = new();
    public LegacyImportPreview? Preview { get; private set; }
    public IReadOnlyList<SelectListItem> ProjectCategories { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TechnicalCategories { get; private set; } = Array.Empty<SelectListItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken);
        if (!ProjectCategoryId.HasValue) ModelState.AddModelError(nameof(ProjectCategoryId), "Select a project category.");
        if (!TechnicalCategoryId.HasValue) ModelState.AddModelError(nameof(TechnicalCategoryId), "Select a technical category.");
        if (Upload is null || Upload.Length == 0) ModelState.AddModelError(nameof(Upload), "Upload a non-empty .xlsx workbook.");
        if (!ModelState.IsValid) return Page();

        var operation = await _preflight.PreviewAsync(
            ProjectCategoryId!.Value,
            TechnicalCategoryId!.Value,
            Upload!,
            cancellationToken);
        if (!operation.Succeeded || operation.Value is null)
        {
            ModelState.AddModelError(string.Empty, operation.UserMessage ?? "The workbook could not be validated.");
            return Page();
        }

        Preview = operation.Value;
        PreviewToken = Preview.Token;
        ViewData["StatusMessage"] = operation.UserMessage;
        return Page();
    }

    public async Task<IActionResult> OnPostCommitAsync(CancellationToken cancellationToken)
    {
        if (!ProjectCategoryId.HasValue || !TechnicalCategoryId.HasValue || PreviewToken == Guid.Empty)
        {
            TempData[FlashMessageKeys.AdminMaintenanceError] = "The validated import is incomplete or has expired. Validate the workbook again.";
            return RedirectToPage();
        }
        if (!ConfirmCommit)
        {
            TempData[FlashMessageKeys.AdminMaintenanceError] = "Confirm that the validated row counts and category selections have been reviewed.";
            return RedirectToPage(new { ProjectCategoryId, TechnicalCategoryId });
        }

        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "legacy-import";
        var result = await _preflight.CommitAsync(
            PreviewToken,
            ProjectCategoryId.Value,
            TechnicalCategoryId.Value,
            actor,
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData[FlashMessageKeys.AdminMaintenanceError] = !string.IsNullOrWhiteSpace(result.TraceId)
                ? $"{result.Message} Trace reference: {result.TraceId}."
                : result.Message;
            return RedirectToPage(new { ProjectCategoryId, TechnicalCategoryId });
        }

        TempData[FlashMessageKeys.AdminMaintenanceSuccess] = result.Message;
        return RedirectToPage(new { ProjectCategoryId, TechnicalCategoryId });
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        if (PreviewToken != Guid.Empty) await _preflight.CancelAsync(PreviewToken, cancellationToken);
        TempData[FlashMessageKeys.AdminMaintenanceSuccess] = "Validated workbook staging was cancelled and removed.";
        return RedirectToPage(new { ProjectCategoryId, TechnicalCategoryId });
    }

    public IActionResult OnGetTemplate()
    {
        using var workbook = ProjectLegacyImportTemplateFactory.CreateWorkbook();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "LegacyProjectsTemplate.xlsx");
    }

    public string StatusTone(LegacyImportRowStatus status) => status switch
    {
        LegacyImportRowStatus.Valid => "success",
        LegacyImportRowStatus.Warning => "warning",
        _ => "danger"
    };

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var projectCategories = await _db.ProjectCategories.AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);
        ProjectCategories = projectCategories
            .Select(category => new SelectListItem(
                category.Name,
                category.Id.ToString(CultureInfo.InvariantCulture),
                ProjectCategoryId == category.Id))
            .ToArray();

        var technicalCategories = await _db.TechnicalCategories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);
        TechnicalCategories = technicalCategories
            .Select(category => new SelectListItem(
                category.Name,
                category.Id.ToString(CultureInfo.InvariantCulture),
                TechnicalCategoryId == category.Id))
            .ToArray();

        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Controlled maintenance",
            Title = "Legacy project import",
            Description = "Validate an approved Excel workbook without database mutation, review every proposed row and then commit one idempotent import.",
            Icon = "bi-database-up",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Maintenance centre",
                    Href = _navigation.GetPath(HttpContext, AdminNavigationKeys.MaintenanceCentre),
                    Icon = "bi-arrow-left"
                },
                new AdminPageActionModel
                {
                    Text = "Download template",
                    Href = Url.Page(null, "Template") ?? "#",
                    Icon = "bi-download",
                    IsPrimary = true
                }
            }
        };
    }
}
