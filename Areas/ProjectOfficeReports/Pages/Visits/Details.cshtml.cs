using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly VisitService _visitService;
    private readonly UserManager<ApplicationUser> _userManager;

    public DetailsModel(VisitService visitService, UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _userManager = userManager;
    }

    public VisitDetails? Visit { get; private set; }

    public VisitPhotosViewModel? PhotoGallery { get; private set; }

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD") || IsProjectOfficeMember();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Visit = await _visitService.GetDetailsAsync(id, cancellationToken);
        if (Visit == null)
        {
            return NotFound();
        }

        PhotoGallery = new VisitPhotosViewModel(id, Visit.Photos, Visit.Visit.CoverPhotoId, false);
        RowVersion = Convert.ToBase64String(Visit.Visit.RowVersion);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string rowVersion, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var rowVersionBytes = DecodeRowVersion(rowVersion);
        if (rowVersionBytes == null)
        {
            TempData["ToastError"] = "We could not verify your request. Please try again.";
            return RedirectToPage(new { id });
        }

        var result = await _visitService.DeleteAsync(id, rowVersionBytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        switch (result.Outcome)
        {
            case VisitDeletionOutcome.Success:
                TempData["ToastMessage"] = "Visit deleted.";
                return RedirectToPage("Index");
            case VisitDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "Another user updated this visit. Please try again.";
                break;
            default:
                TempData["ToastError"] = "Visit not found.";
                break;
        }

        return RedirectToPage(new { id });
    }

    private bool IsProjectOfficeMember()
    {
        return User.IsInRole("ProjectOffice") || User.IsInRole("Project Office");
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
