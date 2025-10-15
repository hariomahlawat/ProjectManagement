using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms;

[Authorize(Roles = "Admin,HoD")]
public class EditModel : PageModel
{
    private readonly SocialMediaPlatformService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(SocialMediaPlatformService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    public class InputModel
    {
        [Required]
        [StringLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(512)]
        public string? Description { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _service.FindAsync(id, cancellationToken);
        if (entity == null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Name = entity.Name,
            Description = entity.Description,
            IsActive = entity.IsActive
        };
        RowVersion = Convert.ToBase64String(entity.RowVersion);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var bytes = DecodeRowVersion(RowVersion);
        if (bytes == null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload the page and try again.");
            return Page();
        }

        var result = await _service.UpdateAsync(id, Input.Name, Input.Description, Input.IsActive, bytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == SocialMediaPlatformMutationOutcome.Success)
        {
            TempData["ToastMessage"] = "Social media platform updated.";
            return RedirectToPage("Index");
        }

        if (result.Outcome == SocialMediaPlatformMutationOutcome.DuplicateName)
        {
            ModelState.AddModelError(nameof(Input.Name), "A social media platform with this name already exists.");
        }
        else if (result.Outcome == SocialMediaPlatformMutationOutcome.ConcurrencyConflict)
        {
            ModelState.AddModelError(string.Empty, "Someone else updated this social media platform. Please reload and try again.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to update the social media platform.");
        }

        return Page();
    }

    private static byte[]? DecodeRowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
