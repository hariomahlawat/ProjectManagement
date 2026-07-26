using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class IndexModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        IArppReadService readService,
        IAuthorizationService authorizationService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    [BindProperty(SupportsGet = true, Name = "fy")]
    public int? FinancialYearStart { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public ArppRegisterResult Register { get; private set; } = new(
        [], [], 0, 0, 0m, 0m, 0, 0, 0);

    public bool CanManage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Register = await _readService.GetRegisterAsync(
            FinancialYearStart,
            Query,
            cancellationToken);

        CanManage = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ManageArpp)).Succeeded;
    }
}
