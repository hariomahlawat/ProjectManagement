using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Ocr;

namespace ProjectManagement.Hosted;

// SECTION: Background worker to backfill OCR text when enabled
public sealed class OcrTextBackfillWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<OcrBackfillOptions> _options;
    private readonly ILogger<OcrTextBackfillWorker> _logger;

    public OcrTextBackfillWorker(
        IServiceProvider serviceProvider,
        IOptions<OcrBackfillOptions> options,
        ILogger<OcrTextBackfillWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("OCR text backfill is disabled; skipping execution.");
            return;
        }

        _logger.LogInformation("OCR text backfill enabled; starting run.");

        using var scope = _serviceProvider.CreateScope();
        var backfillService = scope.ServiceProvider.GetRequiredService<OcrTextBackfillService>();

        try
        {
            await backfillService.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR text backfill encountered an error.");
        }
    }
}
