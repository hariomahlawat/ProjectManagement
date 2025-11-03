using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using System.Net;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public sealed class ViewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage; // adapt to your storage abstraction

        public ViewModel(ApplicationDbContext db, IDocStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        public async Task<IActionResult> OnGetAsync(Guid id)
        {
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.IsActive);

            if (doc is null) return NotFound();

            // Open the underlying binary
            var stream = await _storage.OpenReadAsync(doc.StorageKey);

            // Force inline viewing in browser's PDF viewer
            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{WebUtility.UrlEncode(doc.OriginalFileName ?? $"Document-{doc.Id}.pdf")}\"";

            return File(stream, "application/pdf");
        }
    }
}
