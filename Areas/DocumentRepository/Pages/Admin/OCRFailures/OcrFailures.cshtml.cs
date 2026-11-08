using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin
{
    // Unreadable PDFs is reviewed by those who approve repository deletions.
    [Authorize(Policy = ProjectManagement.Configuration.Policies.Documents.DeleteApprove)]
    public class OcrFailuresModel : PageModel
    {
        private readonly IOcrFailureQueryService _failures;
        private readonly IDocumentOcrService _ocrService;

        public OcrFailuresModel(IOcrFailureQueryService failures, IDocumentOcrService ocrService)
        {
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        }

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = OcrFailureQueryService.DefaultPageSize;

        public IReadOnlyList<int> PageSizes => OcrFailureQueryService.PageSizes;

        public OcrFailurePage Result { get; private set; } =
            new(Array.Empty<OcrFailureRow>(), 0, 1, OcrFailureQueryService.DefaultPageSize);

        public IReadOnlyList<OcrFailureRow> FailedDocuments => Result.Items;

        [TempData]
        public string? StatusMessage { get; set; }

        /// <summary>The admin page header; "Read all again" shows while anything is listed.</summary>
        public AdminPageHeaderModel Header => new()
        {
            Title = "Unreadable PDFs",
            Description = "Documents whose text could not be read, so searching their contents finds nothing. They can still be opened and found by their details.",
            Icon = "bi-file-earmark-break",
            Actions = Result.TotalCount > 0
                ? new[]
                {
                    new AdminPageActionModel
                    {
                        Text = "Read all again",
                        Icon = "bi-arrow-repeat",
                        Href = Url.Page(null, "RequeueAll"),
                        IsPrimary = true,
                        UsesPost = true
                    }
                }
                : Array.Empty<AdminPageActionModel>()
        };

        private string ActorUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

        public async Task OnGetAsync(CancellationToken ct)
        {
            Result = await _failures.GetPageAsync(PageNumber, PageSize, ct);
            PageNumber = Result.Page;
            PageSize = Result.PageSize;
        }

        /// <summary>The list with the current page size, on another page.</summary>
        public string? PageUrl(int page) => Url.Page(null, new
        {
            pageNumber = page > 1 ? page : (int?)null,
            pageSize = PageSize != OcrFailureQueryService.DefaultPageSize ? PageSize : (int?)null
        });

        /// <summary>The list from its first page with another number of rows per page.</summary>
        public string? SizeUrl(int pageSize) => Url.Page(null, new
        {
            pageSize = pageSize != OcrFailureQueryService.DefaultPageSize ? pageSize : (int?)null
        });

        public async Task<IActionResult> OnPostRequeueAsync(Guid id, CancellationToken ct)
        {
            StatusMessage = await _ocrService.RequeueAsync(id, ActorUserId, ct)
                ? "Queued to be read again. The library shows it as not searchable until reading succeeds."
                : "That document no longer exists.";
            return RedirectToPage(new
            {
                pageNumber = PageNumber > 1 ? PageNumber : (int?)null,
                pageSize = PageSize != OcrFailureQueryService.DefaultPageSize ? PageSize : (int?)null
            });
        }

        public async Task<IActionResult> OnPostRequeueAllAsync(CancellationToken ct)
        {
            var count = await _ocrService.RequeueAllFailedAsync(ActorUserId, ct);
            StatusMessage = count == 1
                ? "Queued 1 document to be read again."
                : $"Queued {count} documents to be read again.";
            return RedirectToPage();
        }
    }
}
