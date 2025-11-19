using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Attachments;

[Authorize]
public class ViewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUploadPathResolver _pathResolver;

    public ViewModel(ApplicationDbContext db, IUploadPathResolver pathResolver)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var attachment = await _db.FfcAttachments
            .Include(a => a.Record)
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (attachment is null)
        {
            return NotFound();
        }

        if (attachment.Record is null || attachment.Record.IsDeleted)
        {
            return NotFound();
        }

        var absolutePath = _pathResolver.ToAbsolute(attachment.FilePath);

        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
            ? "application/octet-stream"
            : attachment.ContentType;

        var baseName = string.IsNullOrWhiteSpace(attachment.Caption)
            ? Path.GetFileNameWithoutExtension(absolutePath)
            : attachment.Caption.Trim();
        var extension = Path.GetExtension(absolutePath);
        var downloadName = string.IsNullOrWhiteSpace(baseName)
            ? Path.GetFileName(absolutePath)
            : string.IsNullOrWhiteSpace(extension)
                ? baseName
                : $"{baseName}{extension}";

        var contentDisposition = new System.Net.Mime.ContentDisposition
        {
            Inline = true,
            FileName = downloadName
        };

        // Let the browser preview (inline) and support HTTP range requests
        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{downloadName}\"; filename*=UTF-8''{Uri.EscapeDataString(downloadName)}";

        return new FileStreamResult(stream, contentType)
        {
            EnableRangeProcessing = true
        };

    }
}
