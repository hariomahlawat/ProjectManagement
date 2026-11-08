using Microsoft.Extensions.Options;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Hosted;

/// <summary>
/// Document repository trash retention: a few times a day, permanently deletes documents that
/// have been in the trash longer than DocRepo:TrashRetentionDays (0 turns it off). Each run only
/// purges what is still due, so a run that repeats or overlaps another during a restart does no harm.
/// </summary>
public sealed class DocRepoTrashPurgeWorker : BackgroundService
{
    private const string WorkerKey = "docrepo-trash-purge";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const string SwitchedOffDetail = "Documents stay in the bin until someone deletes them";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DocRepoOptions> _options;
    private readonly ILogger<DocRepoTrashPurgeWorker> _logger;
    private readonly IAdminWorkerStatusRegistry? _status;

    public DocRepoTrashPurgeWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DocRepoOptions> options,
        ILogger<DocRepoTrashPurgeWorker> logger,
        IAdminWorkerStatusRegistry? status = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _status = status;
        _status?.Register(WorkerKey, "Document trash purge", Interval);
        if (_options.CurrentValue.TrashRetentionDays <= 0)
        {
            // Shown as "Switched off" from start-up, not as a job due every 6 hours.
            _status?.MarkSwitchedOff(WorkerKey, SwitchedOffDetail);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let start-up (migrations, seeding) finish first.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var retentionDays = _options.CurrentValue.TrashRetentionDays;
                if (retentionDays > 0)
                {
                    _status?.MarkStarted(WorkerKey);
                    using var scope = _scopeFactory.CreateScope();
                    var run = await scope.ServiceProvider.GetRequiredService<DocRepoTrashPurgeService>().RunExpiredPurgeAsync(stoppingToken);
                    if (run.Purged > 0)
                    {
                        _logger.LogInformation("Purged {Count} document(s) that were in the trash for more than {Days} days.", run.Purged, retentionDays);
                    }

                    _status?.MarkSucceeded(WorkerKey, RunSummary(run, retentionDays));
                }
                else
                {
                    _status?.MarkSwitchedOff(WorkerKey, SwitchedOffDetail);
                }

                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _status?.MarkFailed(WorkerKey, ex);
                _logger.LogError(ex, "Document trash purge failed; it will run again.");
                await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>The last-run line System health shows, naming any documents left in the bin.</summary>
    internal static string RunSummary(DocRepoTrashPurgeRun run, int retentionDays)
    {
        var verb = run.Purged == 1 ? "was" : "were";
        var removed = BackgroundJobMessages.Removed(run.Purged, "document", "documents", $"that {verb} in the bin over {retentionDays} days");
        if (run.Failed <= 0)
        {
            return removed;
        }

        var notRemoved = BackgroundJobMessages.CouldNotRemove(run.Failed, "document", "documents", "stays in the bin", "stay in the bin");
        return run.Purged > 0 ? $"{removed}; {notRemoved}" : notRemoved;
    }
}
