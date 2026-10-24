using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize]
public class DetailsModel : PageModel
{
    // An edit within this long of creation counts as part of recording the visit.
    private static readonly TimeSpan EditGrace = TimeSpan.FromMinutes(2);

    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly UserManager<ApplicationUser> _userManager;

    public DetailsModel(VisitService visitService, VisitTypeService visitTypeService, UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _userManager = userManager;
    }

    public VisitDetails? Visit { get; private set; }

    public VisitPhotosViewModel? PhotoGallery { get; private set; }

    // The photo shown large at the top: the chosen cover, or the first photo when none is chosen.
    public VisitPhoto? CoverPhoto { get; private set; }

    public int CoverPhotoIndex { get; private set; }

    public int TypeColourSlot { get; private set; } = 1;

    public VisitRecordStamp? Recorded { get; private set; }

    public VisitRecordStamp? LastEdited { get; private set; }

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD") || IsProjectOfficeMember();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Visit = await _visitService.GetDetailsAsync(id, cancellationToken);
        if (Visit == null)
        {
            // Keep the 404 status but show the module's own "Visit not found" screen,
            // with a way back to the list, instead of the bare error page.
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        var visit = Visit.Visit;
        PhotoGallery = new VisitPhotosViewModel(id, Visit.Photos, visit.CoverPhotoId, false);
        RowVersion = Convert.ToBase64String(visit.RowVersion);

        CoverPhoto = Visit.Photos.FirstOrDefault(p => p.Id == visit.CoverPhotoId) ?? Visit.Photos.FirstOrDefault();
        CoverPhotoIndex = CoverPhoto is null ? 0 : Visit.Photos.ToList().IndexOf(CoverPhoto);

        var types = await _visitTypeService.GetAllAsync(includeInactive: true, cancellationToken);
        TypeColourSlot = VisitTypeColours.SlotOf(VisitTypeColours.SlotsFor(types), visit.VisitTypeId);

        Recorded = new VisitRecordStamp(await DisplayNameAsync(visit.CreatedByUserId), ToIst(visit.CreatedAtUtc));
        if (visit.LastModifiedAtUtc is { } modified && modified - visit.CreatedAtUtc > EditGrace)
        {
            LastEdited = new VisitRecordStamp(await DisplayNameAsync(visit.LastModifiedByUserId), ToIst(modified));
        }

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
                TempData["ToastSuccess"] = "Visit deleted.";
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

    private async Task<string?> DisplayNameAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName;
    }

    private static DateTimeOffset ToIst(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZoneHelper.GetIst());

    private bool IsProjectOfficeMember()
    {
        return User.IsInRole("Project Office") || User.IsInRole("ProjectOffice");
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

// Who did something to the record and when, in IST. The name is null when the account no longer exists.
public sealed record VisitRecordStamp(string? By, DateTimeOffset AtIst);
