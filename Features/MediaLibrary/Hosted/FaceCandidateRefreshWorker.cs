using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Durable incremental known-person matcher. New or invalidated face embeddings are
/// discovered from MediaFace candidate-search state and processed outside HTTP requests.
/// </summary>
public sealed class FaceCandidateRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceCandidateRefreshWorker> _logger;

    public FaceCandidateRefreshWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceCandidateRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(7), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        _logger.LogInformation(
            "Known-person candidate worker started. BatchSize={BatchSize}, IdleDelaySeconds={IdleDelaySeconds}",
            _options.CandidateRefreshBatchSize,
            _options.CandidateRefreshIdleDelaySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshed = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var suggestions = scope.ServiceProvider.GetRequiredService<IFaceCandidateSuggestionService>();
                refreshed = await suggestions.RefreshUnassignedAsync(
                    Math.Clamp(_options.CandidateRefreshBatchSize, 1, 10_000),
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
                    "Known-person candidate refresh failed. The worker will retry without affecting confirmed identities.");
            }

            var delay = refreshed > 0
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(Math.Clamp(_options.CandidateRefreshIdleDelaySeconds, 1, 3600));
            await Task.Delay(delay, stoppingToken);
        }
    }
}
