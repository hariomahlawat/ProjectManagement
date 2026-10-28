using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Hosted;

// SECTION: Document repository OCR background worker
/// <summary>
/// Works through pending documents one at a time. Each document is claimed and its result saved
/// on its own (see <see cref="DocRepoOcrQueue"/>), so a restart loses at most the document in
/// progress, and overlapping app processes never OCR the same file at once.
/// </summary>
public sealed class DocRepoOcrWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

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
                if (!await ProcessNextAsync(stoppingToken))
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown requested.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing OCR queue");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    // Returns false when there was nothing to do.
    private async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<DocRepoOcrQueue>();
        var runner = scope.ServiceProvider.GetRequiredService<IDocumentOcrRunner>();

        var claim = await queue.ClaimNextAsync(stoppingToken);
        if (claim is null)
        {
            return false;
        }

        var documentId = claim.Document.Id;
        OcrRunResult result;
        try
        {
            result = await runner.RunAsync(claim.Document, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The claim lapses after DocRepoOcrQueue.ClaimLease and the document is picked up again.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error running OCR for document {DocumentId}", documentId);
            result = OcrRunResult.Fail(ex.Message);
        }

        if (!await queue.CompleteAsync(claim, result, stoppingToken))
        {
            _logger.LogInformation(
                "OCR result for document {DocumentId} was dropped: it was re-queued, replaced or deleted while OCR ran.",
                documentId);
        }
        else if (result.Success)
        {
            _logger.LogInformation("OCR succeeded for document {DocumentId}", documentId);
        }
        else
        {
            _logger.LogWarning("OCR failed for document {DocumentId}: {Reason}", documentId, result.Error);
        }

        return true;
    }
}
