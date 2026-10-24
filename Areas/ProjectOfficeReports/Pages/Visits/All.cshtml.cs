using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

// The full register used to live here; it is now the Visits page itself, with paging and
// filters. This keeps old links and bookmarks working, filters included.
[Authorize]
public class AllModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? VisitTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PageSize { get; set; }

    public IActionResult OnGet() => RedirectToPage("Index", new
    {
        VisitTypeId,
        From = string.IsNullOrWhiteSpace(From) ? null : From,
        To = string.IsNullOrWhiteSpace(To) ? null : To,
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q,
        PageNumber = PageNumber is > 1 ? PageNumber : null,
        PageSize = PageSize is 50 or 100 ? PageSize : null
    });
}
