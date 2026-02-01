using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation
{
    // SECTION: Reports page model
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    public sealed class ReportsModel : PageModel
    {
        private readonly IAuthorizationService _authorizationService;

        public ReportsModel(IAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public bool CanManageRecords { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var submitResult = await _authorizationService.AuthorizeAsync(
                User,
                resource: null,
                ProjectOfficeReportsPolicies.SubmitProliferationTracker);
            CanManageRecords = submitResult.Succeeded;
        }
    }
}
