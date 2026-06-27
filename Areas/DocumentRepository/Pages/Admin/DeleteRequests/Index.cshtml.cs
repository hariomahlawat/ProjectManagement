using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.DeleteRequests;

/// <summary>
/// Compatibility route. Repository document deletion decisions are now handled in
/// the central Decision Centre.
/// </summary>
[Authorize(Policy = "DocRepo.DeleteApprove")]
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet()
        => RedirectToPage("/Approvals/Pending/Index", new { Type = "RepositoryDocumentDelete" });
}
