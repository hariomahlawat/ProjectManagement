using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.View)]
public sealed class DownloadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IprAttachmentStorage _storage;
    private readonly ILogger<DownloadModel> _logger;

    public DownloadModel(
        ApplicationDbContext db,
        IprAttachmentStorage storage,
        ILogger<DownloadModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IActionResult> OnGetAsync(
        int iprRecordId,
        int attachmentId,
        CancellationToken cancellationToken)
    {
        if (iprRecordId <= 0 || attachmentId <= 0)
        {
            return NotFound();
        }

        var attachment = await _db.IprAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IprRecordId == iprRecordId &&
                     x.Id == attachmentId &&
                     !x.IsArchived,
                cancellationToken);

        if (attachment is null || string.IsNullOrWhiteSpace(attachment.StorageKey))
        {
            return NotFound();
        }

        Stream stream;
        try
        {
            stream = await _storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Unable to open IPR attachment {AttachmentId} for record {RecordId}.",
                attachmentId,
                iprRecordId);
            return NotFound();
        }

        var downloadName = SanitizeFileName(attachment.OriginalFileName);
        var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
            ? "application/octet-stream"
            : attachment.ContentType;
        var isPdf = string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
                    downloadName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        var result = new FileStreamResult(stream, contentType)
        {
            EnableRangeProcessing = true
        };

        if (isPdf)
        {
            var contentDisposition = new ContentDispositionHeaderValue("inline");
            contentDisposition.SetHttpFileName(downloadName);
            Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();
        }
        else
        {
            result.FileDownloadName = downloadName;
        }

        if (attachment.FileSize > 0)
        {
            Response.Headers[HeaderNames.ContentLength] =
                attachment.FileSize.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static string SanitizeFileName(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return "attachment.pdf";
        }

        var fileName = Path.GetFileName(original);
        return string.IsNullOrWhiteSpace(fileName) ? "attachment.pdf" : fileName;
    }
}
