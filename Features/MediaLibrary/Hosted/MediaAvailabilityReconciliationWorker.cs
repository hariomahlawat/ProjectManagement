using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Reconciles historical missing-content failures after the catalogue becomes
/// physically operational. Temporary schema unavailability never terminates the
/// worker for the lifetime of the process.
/// </summary>
public sealed class MediaAvailabilityReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaAvailabilityReconciliationWorker> _logger;

    public MediaAvailabilityReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MediaAvailabilityReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var schema = scope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
                var schemaStatus = await schema.GetStatusAsync(stoppingToken);
                if (!schemaStatus.IsAvailable || !schemaStatus.IsOperational)
                {
                    _logger.LogInformation(
                        "Media availability reconciliation is waiting for an operational catalogue schema. Reference={Reference}",
                        schemaStatus.DiagnosticReference);
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                var service = scope.ServiceProvider.GetRequiredService<IMediaAvailabilityRecoveryService>();
                var result = await service.ReconcileHistoricalAsync(100, stoppingToken);
                if (result.Examined > 0)
                {
                    _logger.LogInformation(
                        "Media availability reconciliation examined {Examined}, restored {Restored}, marked unavailable {Unavailable}, errors {Errors}",
                        result.Examined, result.Restored, result.MarkedUnavailable, result.Errors);
                }

                if (result.HasMore)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Media availability reconciliation completed");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media availability reconciliation failed; it will retry automatically");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
