using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.View)]
public class DownloadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRootProvider;

    public DownloadModel(ApplicationDbContext db, IUploadRootProvider uploadRootProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
    }

    public async Task<IActionResult> OnGetAsync(int iprRecordId, int attachmentId, CancellationToken cancellationToken)
    {
        if (iprRecordId <= 0 || attachmentId <= 0)
        {
            return NotFound();
        }

        var attachment = await _db.IprAttachments.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IprRecordId == iprRecordId &&
                     x.Id == attachmentId &&
                     !x.IsArchived,
                cancellationToken);

        if (attachment is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(attachment.StorageKey))
        {
            return NotFound();
        }

        var absolutePath = ResolveAbsolutePath(attachment.StorageKey);
        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        var downloadName = SanitizeFileName(attachment.OriginalFileName);
        var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
            ? "application/octet-stream"
            : attachment.ContentType;

        FileStream stream;
        try
        {
            stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch
        {
            return NotFound();
        }

        var result = new FileStreamResult(stream, contentType)
        {
            FileDownloadName = downloadName,
            EnableRangeProcessing = true
        };

        Response.Headers[HeaderNames.ContentLength] = attachment.FileSize.ToString(CultureInfo.InvariantCulture);

        return result;
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var relative = storageKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.Combine(_uploadRootProvider.RootPath, relative);
    }

    private static string SanitizeFileName(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return "attachment";
        }

        var fileName = Path.GetFileName(original);
        return string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName;
    }
}
