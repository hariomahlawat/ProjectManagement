using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Areas.Admin.Pages.Documents;

[Authorize(Roles = "Admin")]
public class IngestExternalPdfsModel : PageModel
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _dbContext;
    private readonly IDocRepoIngestionService _docRepoIngestionService;
    private readonly IprAttachmentStorage _iprAttachmentStorage;
    private readonly IUploadPathResolver _pathResolver;
    private readonly ILogger<IngestExternalPdfsModel>? _logger;

    public IngestExternalPdfsModel(
        ApplicationDbContext dbContext,
        IDocRepoIngestionService docRepoIngestionService,
        IprAttachmentStorage iprAttachmentStorage,
        IUploadPathResolver pathResolver,
        ILogger<IngestExternalPdfsModel>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _docRepoIngestionService = docRepoIngestionService ?? throw new ArgumentNullException(nameof(docRepoIngestionService));
        _iprAttachmentStorage = iprAttachmentStorage ?? throw new ArgumentNullException(nameof(iprAttachmentStorage));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger;
    }

    // SECTION: Result counters
    public int Processed { get; private set; }

    public int SkippedMissing { get; private set; }

    // SECTION: Handlers
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await IngestFfcAttachmentsAsync(cancellationToken);
        await IngestIprAttachmentsAsync(cancellationToken);
        await IngestActivityAttachmentsAsync(cancellationToken);

        TempData["StatusMessage"] = $"Processed {Processed} PDF(s). Skipped {SkippedMissing} missing files.";
        return Page();
    }

    // SECTION: Ingestion helpers
    private async Task IngestFfcAttachmentsAsync(CancellationToken cancellationToken)
    {
        var attachments = await _dbContext.FfcAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ContentType == "application/pdf")
            .ToListAsync(cancellationToken);

        foreach (var attachment in attachments)
        {
            var absolutePath = ResolveAbsolutePath(attachment.FilePath);

            if (string.IsNullOrWhiteSpace(absolutePath) || !System.IO.File.Exists(absolutePath))
            {
                SkippedMissing++;
                continue;
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(absolutePath);
                await _docRepoIngestionService.IngestExternalPdfAsync(
                    stream,
                    Path.GetFileName(absolutePath),
                    "FFC",
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
                Processed++;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to ingest FFC attachment {AttachmentId}.", attachment.Id);
            }
        }
    }

    private async Task IngestIprAttachmentsAsync(CancellationToken cancellationToken)
    {
        var attachments = await _dbContext.IprAttachments
            .AsNoTracking()
            .Where(attachment => !attachment.IsArchived && attachment.ContentType == "application/pdf")
            .ToListAsync(cancellationToken);

        foreach (var attachment in attachments)
        {
            try
            {
                await using var stream = await _iprAttachmentStorage.OpenReadAsync(attachment.StorageKey, cancellationToken);
                await _docRepoIngestionService.IngestExternalPdfAsync(
                    stream,
                    attachment.OriginalFileName,
                    "IPR",
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
                Processed++;
            }
            catch (FileNotFoundException)
            {
                SkippedMissing++;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to ingest IPR attachment {AttachmentId}.", attachment.Id);
            }
        }
    }

    private async Task IngestActivityAttachmentsAsync(CancellationToken cancellationToken)
    {
        var attachments = await _dbContext.ActivityAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ContentType == "application/pdf")
            .ToListAsync(cancellationToken);

        foreach (var attachment in attachments)
        {
            var absolutePath = ResolveAbsolutePath(attachment.StorageKey);
            if (!System.IO.File.Exists(absolutePath))
            {
                SkippedMissing++;
                continue;
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(absolutePath);
                await _docRepoIngestionService.IngestExternalPdfAsync(
                    stream,
                    attachment.OriginalFileName,
                    "Activities",
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
                Processed++;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to ingest activity attachment {AttachmentId}.", attachment.Id);
            }
        }
    }

    private string ResolveAbsolutePath(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return string.Empty;
        }

        try
        {
            return _pathResolver.ToAbsolute(storageKey);
        }
        catch
        {
            return storageKey;
        }
    }
}
