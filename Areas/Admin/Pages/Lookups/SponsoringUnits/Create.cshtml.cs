using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class CreateModel : PageModel
{
    private readonly IAdminMasterDataCommandService _commands;

    public CreateModel(IAdminMasterDataCommandService commands) => _commands = commands;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _commands.CreateSponsoringUnitAsync(
            new FlatLookupCreateCommand(Input.Name, Input.SortOrder),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("Input.Name", result.UserMessage ?? "The sponsoring unit could not be created.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("./Index");
    }

    public sealed class InputModel
    {
        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 1000)]
        public int SortOrder { get; set; }
    }
}
