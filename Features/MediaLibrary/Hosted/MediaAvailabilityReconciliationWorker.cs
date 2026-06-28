using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

public sealed class MediaAvailabilityReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaAvailabilityReconciliationWorker> _logger;

    public MediaAvailabilityReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MediaAvailabilityReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var schema = scope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
                var schemaStatus = await schema.GetStatusAsync(stoppingToken);
                if (!schemaStatus.IsCurrent)
                {
                    _logger.LogInformation("Media availability reconciliation is waiting for the catalogue schema to become current");
                    break;
                }

                var service = scope.ServiceProvider.GetRequiredService<IMediaAvailabilityRecoveryService>();
                var result = await service.ReconcileHistoricalAsync(100, stoppingToken);
                if (result.Examined > 0)
                {
                    _logger.LogInformation(
                        "Media availability reconciliation examined {Examined}, restored {Restored}, marked unavailable {Unavailable}, errors {Errors}",
                        result.Examined, result.Restored, result.MarkedUnavailable, result.Errors);
                }
                if (!result.HasMore) break;
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media availability reconciliation failed. It can be run manually from Media Sources.");
        }
    }
}
