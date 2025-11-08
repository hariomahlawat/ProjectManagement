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
                            document.ExtractedText = CapExtractedText(result.Text);
                            document.OcrStatus = DocOcrStatus.Succeeded;
                            document.OcrFailureReason = null;
                            _logger.LogInformation("OCR succeeded for document {DocumentId}", document.Id);
                        }
                        else
                        {
                            document.ExtractedText = null;
                            document.OcrStatus = DocOcrStatus.Failed;
                            document.OcrFailureReason = TrimForFailure(result.Error);
                            _logger.LogWarning("OCR failed for document {DocumentId}: {Reason}", document.Id, document.OcrFailureReason);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        document.ExtractedText = null;
                        document.OcrStatus = DocOcrStatus.Failed;
                        document.OcrFailureReason = TrimForFailure(ex.Message);
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
