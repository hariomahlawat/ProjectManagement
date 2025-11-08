using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Hosted;

// SECTION: Document repository OCR background worker
public sealed class DocRepoOcrWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocRepoOcrWorker> _logger;

    public DocRepoOcrWorker(IServiceProvider serviceProvider, ILogger<DocRepoOcrWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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
                var runner = scope.ServiceProvider.GetRequiredService<IDocumentOcrRunner>();

                var documents = await db.Documents
                    .Include(d => d.DocumentText)
                    .Where(d => !d.IsDeleted && d.OcrStatus == DocOcrStatus.Pending)
                    .OrderBy(d => d.CreatedAtUtc)
                    .Take(3)
                    .ToListAsync(stoppingToken);

                if (documents.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    continue;
                }

                foreach (var document in documents)
                {
                    document.OcrLastTriedUtc = DateTimeOffset.UtcNow;

                    try
                    {
                        var result = await runner.RunAsync(document, stoppingToken);

                        if (result.Success)
                        {
                            var documentText = document.DocumentText ??= new DocumentText
                            {
                                DocumentId = document.Id,
                                UpdatedAtUtc = DateTime.UtcNow
                            };

                            documentText.OcrText = CapExtractedText(result.Text);
                            documentText.UpdatedAtUtc = DateTime.UtcNow;
                            document.OcrStatus = DocOcrStatus.Succeeded;
                            document.OcrFailureReason = null;
                            _logger.LogInformation("OCR succeeded for document {DocumentId}", document.Id);
                        }
                        else
                        {
                            document.OcrStatus = DocOcrStatus.Failed;
                            document.OcrFailureReason = TrimForFailure(result.Error);
                            if (document.DocumentText is not null)
                            {
                                document.DocumentText.OcrText = null;
                                document.DocumentText.UpdatedAtUtc = DateTime.UtcNow;
                            }
                            _logger.LogWarning("OCR failed for document {DocumentId}: {Reason}", document.Id, document.OcrFailureReason);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        document.OcrStatus = DocOcrStatus.Failed;
                        document.OcrFailureReason = TrimForFailure(ex.Message);
                        if (document.DocumentText is not null)
                        {
                            document.DocumentText.OcrText = null;
                            document.DocumentText.UpdatedAtUtc = DateTime.UtcNow;
                        }
                        _logger.LogError(ex, "Unexpected error running OCR for document {DocumentId}", document.Id);
                    }

                    document.UpdatedAtUtc = DateTime.UtcNow;
                    document.UpdatedByUserId = "ocr-worker";
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown requested.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing OCR queue");
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
}
