using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Refreshes strict unnamed-person groups outside HTTP requests. Grouping remains a review
/// aid only and never creates an identity assignment.
/// </summary>
public sealed class FaceIdentityGroupingRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFaceIdentityGroupingRuntimeState _state;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceIdentityGroupingRefreshWorker> _logger;

    public FaceIdentityGroupingRefreshWorker(
        IServiceScopeFactory scopeFactory,
        IFaceIdentityGroupingRuntimeState state,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceIdentityGroupingRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        var interval = TimeSpan.FromSeconds(
            Math.Clamp(_options.GroupingRefreshIntervalSeconds, 5, 3600));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var grouping = scope.ServiceProvider.GetRequiredService<IFaceIdentityGroupingService>();
                var result = await grouping.GetGroupsAsync(stoppingToken);
                _state.SetResult(result, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _state.SetFailure(exception.GetBaseException().Message, DateTimeOffset.UtcNow);
                _logger.LogWarning(
                    exception,
                    "Background identity grouping failed. The last successful snapshot remains available.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
