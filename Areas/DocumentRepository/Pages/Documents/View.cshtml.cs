using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.View")]
public class ViewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;

    public ViewModel(ApplicationDbContext db, IDocStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        var stream = await _storage.OpenReadAsync(document.StoragePath, cancellationToken);
        var contentDisposition = new System.Net.Mime.ContentDisposition
        {
            Inline = true,
            FileName = document.OriginalFileName
        };

        Response.Headers["Content-Disposition"] = contentDisposition.ToString();
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }
}
