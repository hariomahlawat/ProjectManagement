using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.Admin.Pages.MasterData.ArppReferences;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IArppReferenceDataService _referenceData;

    public IndexModel(IArppReferenceDataService referenceData)
    {
        _referenceData = referenceData ?? throw new ArgumentNullException(nameof(referenceData));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public ArppReferenceDataAdminSnapshot Snapshot { get; private set; } = new([], [], []);

    [BindProperty]
    public ReferenceInputModel Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            ViewData["ReopenReferenceModal"] = true;
            return Page();
        }

        var result = await _referenceData.SaveAsync(
            new ArppReferenceDataSaveCommand(
                Input.Kind,
                Input.Id,
                Input.Value,
                Input.Description,
                Input.SortOrder,
                Input.RowVersion,
                CurrentUserId(),
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            foreach (var pair in result.FieldErrors)
            {
                var key = pair.Key switch
                {
                    nameof(ArppReferenceDataSaveCommand.Value) => "Input.Value",
                    nameof(ArppReferenceDataSaveCommand.Description) => "Input.Description",
                    nameof(ArppReferenceDataSaveCommand.SortOrder) => "Input.SortOrder",
                    _ => string.Empty
                };
                foreach (var message in pair.Value) ModelState.AddModelError(key, message);
            }
            if (result.FieldErrors.Count == 0) ModelState.AddModelError(string.Empty, result.Message);
            await LoadAsync(cancellationToken);
            ViewData["ReopenReferenceModal"] = true;
            return Page();
        }

        TempData["StatusMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetActiveAsync(
        ArppReferenceDataKind kind,
        int id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await _referenceData.SetActiveAsync(
            new ArppReferenceDataActivationCommand(
                kind,
                id,
                isActive,
                rowVersion,
                CurrentUserId(),
                User.Identity?.Name),
            cancellationToken);

        TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Message;
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _referenceData.GetAdminSnapshotAsync(cancellationToken);
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "ARPP configuration",
            Title = "ARPP reference data",
            Description = "Maintain the approved CFA, Fund and DFPDS values available in the ARPP workspace. Deactivate historical values instead of deleting them.",
            Icon = "bi-list-check",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Back to master data",
                    Href = Url.Page("/MasterData/Index", new { area = "Admin" }),
                    Icon = "bi-arrow-left"
                },
                new AdminPageActionModel
                {
                    Text = "Open ARPP register",
                    Href = Url.Page("/ARPP/Index", new { area = "ProjectOfficeReports" }),
                    Icon = "bi-box-arrow-up-right",
                    IsPrimary = true
                }
            }
        };
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public sealed class ReferenceInputModel
    {
        [Required]
        public ArppReferenceDataKind Kind { get; set; }

        public int? Id { get; set; }

        [Required(ErrorMessage = "Enter the reference value.")]
        [StringLength(200)]
        public string Value { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        [Range(0, 9999)]
        public int SortOrder { get; set; }

        public string? RowVersion { get; set; }
    }
    public sealed record ReferencePanelModel(
        ArppReferenceDataKind Kind,
        string Title,
        string Description,
        string Icon,
        IReadOnlyList<ArppReferenceOption> Items);

}
