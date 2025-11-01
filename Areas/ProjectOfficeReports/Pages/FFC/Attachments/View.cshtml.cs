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

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Attachments;

[Authorize]
public class ViewModel(ApplicationDbContext db) : PageModel
{
    private readonly ApplicationDbContext _db = db;

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

        if (!System.IO.File.Exists(attachment.FilePath))
        {
            return NotFound();
        }

        var stream = new FileStream(attachment.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
            ? "application/octet-stream"
            : attachment.ContentType;

        var baseName = string.IsNullOrWhiteSpace(attachment.Caption)
            ? Path.GetFileNameWithoutExtension(attachment.FilePath)
            : attachment.Caption.Trim();
        var extension = Path.GetExtension(attachment.FilePath);
        var downloadName = string.IsNullOrWhiteSpace(baseName)
            ? Path.GetFileName(attachment.FilePath)
            : string.IsNullOrWhiteSpace(extension)
                ? baseName
                : $"{baseName}{extension}";

        var contentDisposition = new System.Net.Mime.ContentDisposition
        {
            Inline = true,
            FileName = downloadName
        };

        Response.Headers["Content-Disposition"] = contentDisposition.ToString();

        return File(stream, contentType)
        {
            EnableRangeProcessing = true
        };
    }
}
