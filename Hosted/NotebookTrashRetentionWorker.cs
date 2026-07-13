using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Notebook;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Hosted;

public sealed class NotebookTrashRetentionWorker : BackgroundService
{
    private const string WorkerKey = "notebook-trash-retention";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotebookTrashOptions> _options;
    private readonly ILogger<NotebookTrashRetentionWorker> _logger;
    private readonly IAdminWorkerStatusRegistry? _status;

    public NotebookTrashRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NotebookTrashOptions> options,
        ILogger<NotebookTrashRetentionWorker> logger,
        IAdminWorkerStatusRegistry? status = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _status = status;
        _status?.Register(WorkerKey, "Notebook trash retention");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _status?.MarkStarted(WorkerKey);
                var options = _options.CurrentValue;
                var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, options.RetentionDays));
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<INotebookService>();
                var purged = await service.PurgeExpiredTrashAsync(cutoff, stoppingToken);
                if (purged > 0) _logger.LogInformation("Purged {Count} expired Notebook Trash items.", purged);
                _status?.MarkSucceeded(WorkerKey, $"Purged {purged} notebook trash item(s).");
                await Task.Delay(options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromHours(6) : options.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _status?.MarkFailed(WorkerKey, ex);
                _logger.LogError(ex, "Notebook Trash retention sweep failed.");
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
    }
}
