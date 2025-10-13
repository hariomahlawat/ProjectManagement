using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize]
public class IndexModel : PageModel
{
    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(VisitService visitService, VisitTypeService visitTypeService, UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? VisitTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<VisitListItem> Items { get; private set; } = Array.Empty<VisitListItem>();

    public IReadOnlyList<SelectListItem> VisitTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public bool CanManage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanManage = IsManager();
        await PopulateVisitTypesAsync(cancellationToken);
        Items = await _visitService.SearchAsync(BuildQuery(), cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string rowVersion, CancellationToken cancellationToken)
    {
        if (!IsManager())
        {
            return Forbid();
        }

        var bytes = DecodeRowVersion(rowVersion);
        if (bytes == null)
        {
            TempData["ToastError"] = "We could not verify your request. Please try again.";
            return RedirectToPage(new { VisitTypeId, From, To, Q });
        }

        var result = await _visitService.DeleteAsync(id, bytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        switch (result.Outcome)
        {
            case VisitDeletionOutcome.Success:
                TempData["ToastMessage"] = "Visit deleted.";
                break;
            case VisitDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The visit was modified by someone else. Please reload the page.";
                break;
            default:
                TempData["ToastError"] = "Visit not found.";
                break;
        }

        return RedirectToPage(new { VisitTypeId, From, To, Q });
    }

    private VisitQueryOptions BuildQuery()
    {
        return new VisitQueryOptions(VisitTypeId, ParseDate(From), ParseDate(To), Q);
    }

    private async Task PopulateVisitTypesAsync(CancellationToken cancellationToken)
    {
        var types = await _visitTypeService.GetAllAsync(includeInactive: false, cancellationToken);
        var list = new List<SelectListItem>
        {
            new("All types", string.Empty)
        };

        foreach (var type in types)
        {
            list.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = VisitTypeId.HasValue && type.Id == VisitTypeId.Value
            });
        }

        VisitTypeOptions = list;
    }

    private bool IsManager()
    {
        return User.IsInRole("Admin") || User.IsInRole("HoD") || IsProjectOfficeMember();
    }

    private bool IsProjectOfficeMember()
    {
        return User.IsInRole("ProjectOffice") || User.IsInRole("Project Office");
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateOnly.TryParse(value, out date))
        {
            return date;
        }

        return null;
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
