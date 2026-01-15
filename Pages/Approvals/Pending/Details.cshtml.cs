using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Approvals;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Approvals.Pending;

[Authorize(Roles = "Admin,HoD")]
public class DetailsModel : PageModel
{
    // SECTION: Dependencies
    private readonly IApprovalQueueService _queueService;

    public DetailsModel(IApprovalQueueService queueService)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
    }

    // SECTION: View model state
    public ApprovalQueueDetailVm Detail { get; private set; } = default!;

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    public string? ErrorMessage => TempData["Error"] as string;

    public string? SuccessMessage => TempData["Flash"] as string;

    public async Task<IActionResult> OnGetAsync(string type, string id, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalQueueType>(type, true, out var parsedType))
        {
            return NotFound();
        }

        var detail = await _queueService.GetDetailAsync(parsedType, id, User, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        Input = new DecisionInput
        {
            ApprovalType = detail.Item.ApprovalType,
            RequestId = detail.Item.RequestId,
            RowVersion = detail.Item.ConcurrencyToken,
            ReturnUrl = Url.Page("/Approvals/Pending/Details", new { type, id }) ?? string.Empty
        };

        return Page();
    }

    // SECTION: Decision input model
    public sealed class DecisionInput
    {
        [Required]
        public ApprovalQueueType ApprovalType { get; set; }

        [Required]
        public string RequestId { get; set; } = string.Empty;

        [Required]
        public string Decision { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Remarks { get; set; }

        public string? RowVersion { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
