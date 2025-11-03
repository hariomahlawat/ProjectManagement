using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public sealed class DownloadModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage;

        public DownloadModel(ApplicationDbContext db, IDocStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        public async Task<IActionResult> OnGetAsync(Guid id)
        {
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc is null) return NotFound();

            var stream = await _storage.OpenReadAsync(doc.StorageKey);

            // Return as attachment so browser downloads it
            var downloadName = string.IsNullOrWhiteSpace(doc.OriginalFileName)
                ? $"Document-{doc.Id}.pdf"
                : doc.OriginalFileName;

            return File(stream, "application/pdf", downloadName);
        }
    }
}
