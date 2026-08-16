using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Reports.ArppFyProjectUpdate;

namespace ProjectManagement.Pages.Projects.Reports;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class IndexModel : PageModel
{
    private readonly IArppFyProjectUpdateService _reportService;

    public IndexModel(IArppFyProjectUpdateService reportService)
        => _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));

    public IReadOnlyList<int> AvailableFinancialYears { get; private set; } = Array.Empty<int>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => AvailableFinancialYears = await _reportService.GetAvailableFinancialYearsAsync(cancellationToken);
}
