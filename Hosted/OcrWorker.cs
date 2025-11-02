using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Hosted;

public sealed class OcrWorker : BackgroundService
{
    private const string SystemActor = "system@ocr";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OcrWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(15);

    public OcrWorker(IServiceProvider serviceProvider, ILogger<OcrWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OCR worker started");
        var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IDocStorage>();
                var ocr = scope.ServiceProvider.GetRequiredService<IOcrEngine>();
                var audit = scope.ServiceProvider.GetRequiredService<IDocRepoAuditService>();

                var document = await db.Documents
                    .Where(d => d.OcrStatus == OcrStatus.Queued && d.IsActive)
                    .OrderBy(d => d.OcrLastTriedUtc ?? DateTimeOffset.MinValue)
                    .FirstOrDefaultAsync(stoppingToken);

                if (document is null)
                {
                    continue;
                }

                document.OcrLastTriedUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(stoppingToken);

                try
                {
                    await using var stream = await storage.OpenReadAsync(document.StoragePath, stoppingToken);
                    var text = await ocr.ExtractAsync(stream, stoppingToken);
                    document.ExtractedText = text;
                    document.OcrStatus = OcrStatus.Succeeded;
                    document.OcrFailureReason = null;
                    await db.SaveChangesAsync(stoppingToken);

                    await audit.WriteAsync(document.Id, SystemActor, "OcrSucceeded", new { document.Id, length = text?.Length ?? 0 }, stoppingToken);
                }
                catch (Exception ex)
                {
                    document.OcrStatus = OcrStatus.Failed;
                    document.OcrFailureReason = ex.Message.TruncateOrNull(500);
                    await db.SaveChangesAsync(stoppingToken);

                    await audit.WriteAsync(document.Id, SystemActor, "OcrFailed", new { document.Id, error = ex.Message }, stoppingToken);
                    _logger.LogWarning(ex, "OCR failed for document {DocumentId}", document.Id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OCR loop");
            }
        }

        _logger.LogInformation("OCR worker stopped");
    }
}

file static class StringExtensions
{
    public static string? TruncateOrNull(this string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
