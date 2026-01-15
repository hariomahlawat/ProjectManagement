using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services.Approvals;
using ProjectManagement.ViewModels.Navigation;

namespace ProjectManagement.ViewComponents;

public sealed class PendingApprovalsBadgeViewComponent : ViewComponent
{
    // SECTION: Dependencies
    private readonly IApprovalQueueService _queueService;

    public PendingApprovalsBadgeViewComponent(IApprovalQueueService queueService)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
    }

    // SECTION: View component entry point
    public async Task<IViewComponentResult> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var count = await _queueService.GetPendingCountAsync(HttpContext.User, cancellationToken);
        var model = BuildViewModel(count);

        return View(model);
    }

    // SECTION: View model builder
    private static PendingApprovalsBadgeViewModel BuildViewModel(int count)
    {
        if (count <= 0)
        {
            return new PendingApprovalsBadgeViewModel
            {
                ShowBadge = false,
            };
        }

        var displayText = count > 99 ? "99+" : count.ToString();

        return new PendingApprovalsBadgeViewModel
        {
            ShowBadge = true,
            DisplayText = displayText,
            AriaLabel = $"Pending approvals: {count}"
        };
    }
}
