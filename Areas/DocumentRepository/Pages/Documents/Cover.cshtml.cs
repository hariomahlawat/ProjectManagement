using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    // The first-page cover of a document, for the library's cover views. Same access rule as the
    // PDF itself; 404 when the document is hidden from this user or has no cover (the page then
    // shows the type-coloured cover). Not audited: it shows what the list already shows.
    [Authorize(Policy = "DocRepo.View")]
    public sealed class CoverModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocRepoAccess _access;
        private readonly IDocRepoCoverService _covers;

        public CoverModel(ApplicationDbContext db, IDocRepoAccess access, IDocRepoCoverService covers)
        {
            _db = db;
            _access = access;
            _covers = covers;
        }

        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            var canViewInactive = await _access.CanViewInactiveAsync(User);
            var document = await _access.Readable(_db.Documents.AsNoTracking(), canViewInactive)
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (document is null)
            {
                return NotFound();
            }

            var path = await _covers.GetCoverAsync(document, cancellationToken);
            if (path is null)
            {
                return NotFound();
            }

            // Like the PDF: never kept in the browser cache on shared machines.
            Response.Headers[HeaderNames.CacheControl] = "private, no-store";
            return PhysicalFile(path, "image/jpeg");
        }
    }
}
