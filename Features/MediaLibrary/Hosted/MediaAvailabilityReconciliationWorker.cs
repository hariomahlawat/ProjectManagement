using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Continuously converts historical missing-content processing failures into the
/// authoritative MediaAsset availability state. Work is bounded and idempotent.
/// </summary>
public sealed class MediaAvailabilityReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromMinutes(1);

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
        await Task.Delay(StartupDelay, stoppingToken);

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
                    await Task.Delay(ErrorDelay, stoppingToken);
                    continue;
                }

                var service = scope.ServiceProvider.GetRequiredService<IMediaAvailabilityRecoveryService>();
                var result = await service.ReconcileHistoricalAsync(250, stoppingToken);
                if (result.Examined > 0)
                {
                    _logger.LogInformation(
                        "Availability reconciliation examined {Examined}, restored {Restored}, marked unavailable {Unavailable}, temporary {Temporary}, errors {Errors}",
                        result.Examined,
                        result.Restored,
                        result.MarkedUnavailable,
                        result.TemporarilyUnavailable,
                        result.Errors);
                }

                await Task.Delay(result.HasMore ? BatchDelay : IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media availability reconciliation failed; it will retry automatically");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }
}
