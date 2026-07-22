using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.Categories;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class DeleteModel : PageModel
{
    private readonly IMasterDataAdministrationQueryService _query;
    private readonly IAdminMasterDataCommandService _commands;

    public DeleteModel(
        IMasterDataAdministrationQueryService query,
        IAdminMasterDataCommandService commands)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public CategoryImpact? Impact { get; private set; }

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    [BindProperty]
    public string Confirmation { get; set; } = string.Empty;

    public bool CanDelete => Impact is { DirectUsageCount: 0, DirectChildCount: 0 };

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Impact = await _query.GetCategoryImpactAsync(MasterDataCategoryKind.Project, id, cancellationToken);
        if (Impact is null) return NotFound();
        RowVersion = Impact.RowVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        Impact = await _query.GetCategoryImpactAsync(MasterDataCategoryKind.Project, id, cancellationToken);
        if (Impact is null) return NotFound();

        if (!CanDelete)
        {
            ModelState.AddModelError(string.Empty, "This category is referenced by projects or contains child categories. Deactivate it instead of deleting it.");
            return Page();
        }

        if (!string.Equals(Confirmation?.Trim(), Impact.Name, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Confirmation), $"Type '{Impact.Name}' exactly to confirm deletion.");
            return Page();
        }

        if (!TryDecode(RowVersion, out var rowVersion))
        {
            ModelState.AddModelError(string.Empty, "The record version is invalid. Reload the page and try again.");
            return Page();
        }

        var result = await _commands.DeleteProjectCategoryAsync(id, rowVersion, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.UserMessage ?? "The category could not be deleted.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("Index");
    }

    private static bool TryDecode(string? value, out byte[] rowVersion)
    {
        try
        {
            rowVersion = string.IsNullOrWhiteSpace(value) ? Array.Empty<byte>() : Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            rowVersion = Array.Empty<byte>();
            return false;
        }
    }
}
