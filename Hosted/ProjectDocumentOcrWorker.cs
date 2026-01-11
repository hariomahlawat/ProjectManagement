using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Data.Projects;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Hosted;

// SECTION: Project document OCR background worker
public sealed class ProjectDocumentOcrWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectDocumentOcrWorker> _logger;

    public ProjectDocumentOcrWorker(IServiceProvider serviceProvider, ILogger<ProjectDocumentOcrWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // SECTION: Poll loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var runner = scope.ServiceProvider.GetRequiredService<IProjectDocumentOcrRunner>();
                var textExtractor = scope.ServiceProvider.GetRequiredService<IProjectDocumentTextExtractor>();

                var documents = await db.ProjectDocuments
                    .Include(d => d.DocumentText)
                    .Where(d => d.Status == ProjectDocumentStatus.Published && !d.IsArchived)
                    .Where(d => d.OcrStatus == ProjectDocumentOcrStatus.Pending)
                    .OrderBy(d => d.UploadedAtUtc)
                    .Take(5)
                    .ToListAsync(stoppingToken);

                if (documents.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    continue;
                }

                foreach (var document in documents)
                {
                    var now = DateTimeOffset.UtcNow;
                    document.OcrLastTriedUtc = now;

                    var refreshSearchVector = false;

                    try
                    {
                        if (IsPdfContentType(document.ContentType))
                        {
                            // SECTION: PDF OCR pipeline
                            var result = await runner.RunAsync(document, stoppingToken);

                            if (result.Success)
                            {
                                // SECTION: Guard against ocrmypdf banner-only output
                                var normalizedOcrText = result.Text?.Trim();
                                var containsOnlySkipBanners = ContainsOnlySkipBanners(normalizedOcrText);

                                if (containsOnlySkipBanners)
                                {
                                    document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                                    document.OcrFailureReason = "OCR produced only a skip message.";
                                    UpdateDocumentText(db, document, null, now, ref refreshSearchVector);
                                    _logger.LogWarning(
                                        "OCR result for project document {DocumentId} contained only skip banners",
                                        document.Id);
                                }
                                else
                                {
                                    UpdateDocumentText(db, document, CapExtractedText(normalizedOcrText), now, ref refreshSearchVector);
                                    document.OcrStatus = ProjectDocumentOcrStatus.Succeeded;
                                    document.OcrFailureReason = null;
                                    _logger.LogInformation("OCR succeeded for project document {DocumentId}", document.Id);
                                }
                            }
                            else
                            {
                                document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                                document.OcrFailureReason = TrimForFailure(result.Error);
                                UpdateDocumentText(db, document, null, now, ref refreshSearchVector);
                                _logger.LogWarning(
                                    "OCR failed for project document {DocumentId}: {Reason}",
                                    document.Id,
                                    document.OcrFailureReason);
                            }
                        }
                        else
                        {
                            // SECTION: Office document extraction pipeline
                            var extractionResult = await textExtractor.ExtractAsync(document, stoppingToken);

                            if (extractionResult.Status == ProjectDocumentTextExtractionStatus.NotApplicable)
                            {
                                document.OcrStatus = ProjectDocumentOcrStatus.Skipped;
                                document.OcrFailureReason = null;
                                _logger.LogInformation(
                                    "Skipping OCR for project document {DocumentId} because content type {ContentType} is not supported.",
                                    document.Id,
                                    document.ContentType);
                            }
                            else if (extractionResult.Status == ProjectDocumentTextExtractionStatus.Failed)
                            {
                                document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                                document.OcrFailureReason = TrimForFailure(extractionResult.Error);
                                UpdateDocumentText(db, document, null, now, ref refreshSearchVector);
                                _logger.LogWarning(
                                    "Text extraction failed for project document {DocumentId}: {Reason}",
                                    document.Id,
                                    document.OcrFailureReason);
                            }
                            else
                            {
                                if (!string.IsNullOrWhiteSpace(extractionResult.ConversionError))
                                {
                                    _logger.LogWarning(
                                        "PDF conversion warning for project document {DocumentId}: {Reason}",
                                        document.Id,
                                        extractionResult.ConversionError);
                                }

                                var combinedText = extractionResult.ExtractedText;

                                if (!string.IsNullOrWhiteSpace(extractionResult.PdfDerivativeStorageKey))
                                {
                                    var derivativeDocument = new ProjectDocument
                                    {
                                        Id = document.Id,
                                        StorageKey = extractionResult.PdfDerivativeStorageKey
                                    };

                                    var ocrResult = await runner.RunAsync(derivativeDocument, stoppingToken);
                                    if (ocrResult.Success)
                                    {
                                        var normalizedOcrText = ocrResult.Text?.Trim();
                                        if (ContainsOnlySkipBanners(normalizedOcrText))
                                        {
                                            normalizedOcrText = null;
                                            _logger.LogWarning(
                                                "OCR result for project document {DocumentId} PDF derivative contained only skip banners",
                                                document.Id);
                                        }

                                        combinedText = CombineText(combinedText, normalizedOcrText);
                                    }
                                    else if (string.IsNullOrWhiteSpace(combinedText))
                                    {
                                        document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                                        document.OcrFailureReason = TrimForFailure(ocrResult.Error);
                                        UpdateDocumentText(db, document, null, now, ref refreshSearchVector);
                                        _logger.LogWarning(
                                            "OCR failed for project document {DocumentId}: {Reason}",
                                            document.Id,
                                            document.OcrFailureReason);
                                    }
                                    else
                                    {
                                        _logger.LogWarning(
                                            "OCR failed for project document {DocumentId} PDF derivative: {Reason}",
                                            document.Id,
                                            TrimForFailure(ocrResult.Error));
                                    }
                                }

                                if (document.OcrStatus != ProjectDocumentOcrStatus.Failed)
                                {
                                    if (string.IsNullOrWhiteSpace(combinedText))
                                    {
                                        if (!string.IsNullOrWhiteSpace(extractionResult.ConversionError))
                                        {
                                            document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                                            document.OcrFailureReason = TrimForFailure(extractionResult.ConversionError);
                                            UpdateDocumentText(db, document, null, now, ref refreshSearchVector);
                                            _logger.LogWarning(
                                                "PDF conversion failed for project document {DocumentId}: {Reason}",
                                                document.Id,
                                                document.OcrFailureReason);
                                        }
                                        else
                                        {
                                            document.OcrStatus = ProjectDocumentOcrStatus.Skipped;
                                            document.OcrFailureReason = null;
                                            UpdateDocumentText(db, document, null, now, ref refreshSearchVector);
                                            _logger.LogInformation(
                                                "No extractable text for project document {DocumentId}; marking OCR as skipped",
                                                document.Id);
                                        }
                                    }
                                    else
                                    {
                                        UpdateDocumentText(db, document, CapExtractedText(combinedText), now, ref refreshSearchVector);
                                        document.OcrStatus = ProjectDocumentOcrStatus.Succeeded;
                                        document.OcrFailureReason = null;
                                        _logger.LogInformation(
                                            "Text extraction succeeded for project document {DocumentId}",
                                            document.Id);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                        document.OcrFailureReason = TrimForFailure(ex.Message);

                        if (document.DocumentText is not null)
                        {
                            document.DocumentText.OcrText = null;
                            document.DocumentText.UpdatedAtUtc = DateTimeOffset.UtcNow;
                            refreshSearchVector = true;
                        }

                        _logger.LogError(
                            ex,
                            "Unexpected error running OCR for project document {DocumentId}",
                            document.Id);
                    }

                    await db.SaveChangesAsync(stoppingToken);

                    if (refreshSearchVector)
                    {
                        await RefreshSearchVectorAsync(db, document.Id, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing project document OCR queue");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    // SECTION: Helpers
    private static string? TrimForFailure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length > 1000 ? value[..1000] : value;
    }

    private static string? CapExtractedText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Length > 200_000 ? text[..200_000] : text;
    }

    private static bool ContainsOnlySkipBanners(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hasContent = false;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = TrimLeadingBom(rawLine).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if ((line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) ||
                (line.StartsWith("(", StringComparison.Ordinal) && line.EndsWith(")", StringComparison.Ordinal)))
            {
                line = line.TrimStart('[', '(').TrimEnd(']', ')').Trim();
            }

            if (line.Length == 0)
            {
                continue;
            }

            hasContent = true;

            var isSkipBanner = line.IndexOf("OCR skipped on page", StringComparison.OrdinalIgnoreCase) >= 0;
            var isPriorOcrBanner = line.IndexOf("Prior OCR", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isSkipBanner && !isPriorOcrBanner)
            {
                return false;
            }
        }

        return hasContent;
    }

    private static string TrimLeadingBom(string value)
    {
        return value.Length > 0 ? value.TrimStart('\ufeff') : value;
    }

    private static string? CombineText(string? primary, string? secondary)
    {
        var normalizedPrimary = string.IsNullOrWhiteSpace(primary) ? null : primary.Trim();
        var normalizedSecondary = string.IsNullOrWhiteSpace(secondary) ? null : secondary.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPrimary))
        {
            return normalizedSecondary;
        }

        if (string.IsNullOrWhiteSpace(normalizedSecondary))
        {
            return normalizedPrimary;
        }

        return $"{normalizedPrimary}\n\n{normalizedSecondary}";
    }

    private static bool IsPdfContentType(string? contentType)
    {
        return string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static void UpdateDocumentText(
        ApplicationDbContext db,
        ProjectDocument document,
        string? text,
        DateTimeOffset now,
        ref bool refreshSearchVector)
    {
        if (document.DocumentText is null && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var entry = document.DocumentText ?? new ProjectDocumentText
        {
            ProjectDocumentId = document.Id,
            UpdatedAtUtc = now
        };

        if (document.DocumentText is null)
        {
            db.ProjectDocumentTexts.Add(entry);
            document.DocumentText = entry;
        }

        entry.OcrText = text;
        entry.UpdatedAtUtc = now;
        refreshSearchVector = true;
    }

    // SECTION: Full-text search maintenance helpers
    private async Task RefreshSearchVectorAsync(ApplicationDbContext db, int documentId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
        {
            return;
        }

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""ProjectDocuments"" AS doc
                SET ""SearchVector"" =
                    setweight(to_tsvector('english', coalesce(doc.""Title"", '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(doc.""Description"", '')), 'B') ||
                    setweight(to_tsvector('english', coalesce(doc.""OriginalFileName"", '')), 'C') ||
                    setweight(to_tsvector('english', coalesce((
                        SELECT ""StageCode"" FROM ""ProjectStages"" WHERE ""Id"" = doc.""StageId""
                    ), '')), 'C') ||
                    setweight(to_tsvector('english', coalesce((
                        SELECT ""OcrText"" FROM ""ProjectDocumentTexts"" WHERE ""ProjectDocumentId"" = doc.""Id""
                    ), '')), 'D')
                WHERE doc.""Id"" = {documentId};
            ", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh search vector for project document {DocumentId}", documentId);
        }
    }
}
