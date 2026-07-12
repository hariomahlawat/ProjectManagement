using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Incrementally discovers photographs that need the currently approved face-analysis
/// model pair and places idempotent jobs on the shared media-processing queue.
/// </summary>
public sealed class FaceAnalysisQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceAnalysisQueueWorker> _logger;

    public FaceAnalysisQueueWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceAnalysisQueueWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        _logger.LogInformation(
            "Face-analysis queue worker started. BatchSize={BatchSize}, IdleDelaySeconds={IdleDelaySeconds}",
            _options.BatchSize,
            _options.IdleDelaySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var queued = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IFaceQueueService>();
                queued = await queue.QueueEligibleAsync(
                    Math.Clamp(_options.BatchSize, 1, 100),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Face-analysis queue discovery failed. The worker will retry without affecting Photos availability.");
            }

            var delay = queued > 0
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(Math.Clamp(_options.IdleDelaySeconds, 5, 3600));
            await Task.Delay(delay, stoppingToken);
        }
    }
}
