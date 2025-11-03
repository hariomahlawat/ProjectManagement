using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public class ReaderModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public ReaderModel(ApplicationDbContext db)
        {
            _db = db;
        }

        // what we show in the heading
        public string DocumentTitle { get; private set; } = "Document";

        // iframe url – this is the same endpoint that the browser can render nicely
        public string ViewUrl { get; private set; } = string.Empty;

        // direct download url
        public string DownloadUrl { get; private set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (doc == null)
            {
                return NotFound();
            }

            DocumentTitle = string.IsNullOrWhiteSpace(doc.Subject)
                ? "Document"
                : doc.Subject;

            // these two pages already exist in your module
            ViewUrl = Url.Page("./View", new { id }) ?? string.Empty;
            DownloadUrl = Url.Page("./Download", new { id }) ?? string.Empty;

            return Page();
        }
    }
}
