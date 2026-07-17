using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.Edit)]
public sealed class ManageModel : PageModel
{
    public IActionResult OnGet(int? id = null, int? editId = null, string? query = null)
    {
        var recordId = id is > 0 ? id : editId is > 0 ? editId : null;

        return RedirectToPage("./Index", new
        {
            tab = "records",
            mode = recordId.HasValue ? "edit" : "create",
            id = recordId,
            query
        });
    }
}
