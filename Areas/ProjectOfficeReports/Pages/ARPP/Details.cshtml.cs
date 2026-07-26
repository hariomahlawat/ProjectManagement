using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class DetailsModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IAuthorizationService _authorizationService;

    public DetailsModel(
        IArppReadService readService,
        IAuthorizationService authorizationService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public ArppIssueDetails Issue { get; private set; } = default!;
    public bool CanManage { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var issue = await _readService.GetIssueAsync(id, cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        Issue = issue;
        CanManage = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ManageArpp)).Succeeded;
        return Page();
    }
}
