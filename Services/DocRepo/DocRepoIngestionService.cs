using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Ingestion service implementation
public sealed class DocRepoIngestionService : IDocRepoIngestionService
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;
    private readonly DocRepoOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DocRepoIngestionService>? _logger;

    public DocRepoIngestionService(
        ApplicationDbContext db,
        IDocStorage storage,
        IOptions<DocRepoOptions> options,
        IClock clock,
        ILogger<DocRepoIngestionService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    public async Task<Guid> IngestExternalPdfAsync(
        Stream pdfStream,
        string originalFileName,
        string sourceModule,
        string sourceItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        if (!_options.EnableIngestion)
        {
            _logger?.LogDebug(
                "DocRepo ingestion disabled. Skipping file from module {Module} item {ItemId}.",
                sourceModule,
                sourceItemId);
            return Guid.Empty;
        }

        if (string.IsNullOrWhiteSpace(sourceModule))
        {
            throw new ArgumentException("Source module is required.", nameof(sourceModule));
        }

        if (string.IsNullOrWhiteSpace(sourceItemId))
        {
            throw new ArgumentException("Source item id is required.", nameof(sourceItemId));
        }

        var normalizedModule = sourceModule.Trim();
        var normalizedItemId = sourceItemId.Trim();
        var safeFileName = Path.GetFileName(string.IsNullOrWhiteSpace(originalFileName)
            ? "document.pdf"
            : originalFileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = "document.pdf";
        }

        await using var buffer = new MemoryStream();
        if (pdfStream.CanSeek)
        {
            pdfStream.Seek(0, SeekOrigin.Begin);
        }

        await pdfStream.CopyToAsync(buffer, cancellationToken);
        buffer.Seek(0, SeekOrigin.Begin);

        var sha256Hex = ComputeSha256(buffer);
        buffer.Seek(0, SeekOrigin.Begin);

        var existingDocument = await _db.Documents
            .Include(d => d.ExternalLinks)
            .FirstOrDefaultAsync(
                d => d.Sha256 == sha256Hex && !d.IsDeleted,
                cancellationToken);

        var now = _clock.UtcNow;
        var nowUtc = now.UtcDateTime;
        var createdBy = string.IsNullOrWhiteSpace(_options.IngestionUserId)
            ? "system"
            : _options.IngestionUserId.Trim();

        if (existingDocument != null)
        {
            var alreadyLinked = existingDocument.ExternalLinks.Any(link =>
                link.SourceModule.Equals(normalizedModule, StringComparison.OrdinalIgnoreCase) &&
                link.SourceItemId.Equals(normalizedItemId, StringComparison.Ordinal));

            if (!alreadyLinked)
            {
                var externalLink = new DocRepoExternalLink
                {
                    Id = Guid.NewGuid(),
                    DocumentId = existingDocument.Id,
                    SourceModule = normalizedModule,
                    SourceItemId = normalizedItemId,
                    CreatedAtUtc = now,
                };

                _db.DocRepoExternalLinks.Add(externalLink);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return existingDocument.Id;
        }

        var officeCategoryId = _options.IngestionOfficeCategoryId
            ?? throw new InvalidOperationException(
                "DocRepo ingestion requires DocRepo:IngestionOfficeCategoryId to be configured.");
        var documentCategoryId = _options.IngestionDocumentCategoryId
            ?? throw new InvalidOperationException(
                "DocRepo ingestion requires DocRepo:IngestionDocumentCategoryId to be configured.");

        buffer.Seek(0, SeekOrigin.Begin);
        var storagePath = await _storage.SaveAsync(buffer, safeFileName, nowUtc, cancellationToken);

        buffer.Seek(0, SeekOrigin.Begin);
        var fileSize = buffer.Length;
        var subject = Path.GetFileNameWithoutExtension(safeFileName);
        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = safeFileName;
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            OfficeCategoryId = officeCategoryId,
            DocumentCategoryId = documentCategoryId,
            OriginalFileName = safeFileName,
            FileSizeBytes = fileSize,
            Sha256 = sha256Hex,
            StoragePath = storagePath,
            MimeType = "application/pdf",
            IsActive = true,
            CreatedByUserId = createdBy,
            CreatedAtUtc = nowUtc,
            UpdatedByUserId = createdBy,
            UpdatedAtUtc = nowUtc,
            OcrStatus = DocOcrStatus.Pending,
        };

        var link = new DocRepoExternalLink
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            SourceModule = normalizedModule,
            SourceItemId = normalizedItemId,
            CreatedAtUtc = now,
        };

        await _db.Documents.AddAsync(document, cancellationToken);
        await _db.DocRepoExternalLinks.AddAsync(link, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Ingested external PDF for module {Module} item {ItemId} into document {DocumentId}.",
            normalizedModule,
            normalizedItemId,
            document.Id);

        return document.Id;
    }

    private static string ComputeSha256(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
