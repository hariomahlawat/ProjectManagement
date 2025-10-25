using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training.Components.TrainingApprovalsBadge;

public sealed class TrainingApprovalsBadgeViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuthorizationService _authorizationService;

    public TrainingApprovalsBadgeViewComponent(
        ApplicationDbContext dbContext,
        IAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return View(0);
        }

        var authorizationResult = await _authorizationService.AuthorizeAsync(
            HttpContext.User,
            resource: null,
            ProjectOfficeReportsPolicies.ApproveTrainingTracker);

        if (!authorizationResult.Succeeded)
        {
            return View(0);
        }

        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        var pendingCount = await _dbContext.TrainingDeleteRequests
            .AsNoTracking()
            .CountAsync(request => request.Status == TrainingDeleteRequestStatus.Pending, cancellationToken);

        const string viewPath = "~/Areas/ProjectOfficeReports/Pages/Training/Components/TrainingApprovalsBadge/Default.cshtml";

        return View(viewPath, pendingCount);
    }
}
