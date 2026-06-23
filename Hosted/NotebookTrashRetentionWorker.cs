using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Hosted;

public sealed class NotebookTrashRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotebookTrashOptions> _options;
    private readonly ILogger<NotebookTrashRetentionWorker> _logger;

    public NotebookTrashRetentionWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<NotebookTrashOptions> options, ILogger<NotebookTrashRetentionWorker> logger)
    { _scopeFactory = scopeFactory; _options = options; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = _options.CurrentValue;
                var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, options.RetentionDays));
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<INotebookService>();
                var purged = await service.PurgeExpiredTrashAsync(cutoff, stoppingToken);
                if (purged > 0) _logger.LogInformation("Purged {Count} expired Notebook Trash items.", purged);
                await Task.Delay(options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromHours(6) : options.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notebook Trash retention sweep failed.");
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
    }
}
