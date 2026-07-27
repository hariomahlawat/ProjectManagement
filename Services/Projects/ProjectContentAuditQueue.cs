using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Non-blocking audit dispatcher for project-content edits.
/// Database persistence remains in the request transaction; audit delivery is performed
/// in a fresh dependency-injection scope so a slow audit sink cannot hold the user's save.
/// </summary>
public interface IProjectContentAuditQueue
{
    bool TryEnqueue(ProjectContentAuditEntry entry);
}

public sealed record ProjectContentAuditEntry(
    string Action,
    string Message,
    string UserId,
    string UserDisplay,
    IDictionary<string, string?> Data);

public sealed class ProjectContentAuditQueue : BackgroundService, IProjectContentAuditQueue
{
    private const int Capacity = 2048;

    private readonly Channel<ProjectContentAuditEntry> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProjectContentAuditQueue> _logger;

    public ProjectContentAuditQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<ProjectContentAuditQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<ProjectContentAuditEntry>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public bool TryEnqueue(ProjectContentAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_channel.Writer.TryWrite(entry))
        {
            return true;
        }

        _logger.LogError(
            "Project-content audit queue is full. Action={Action}, UserId={UserId}",
            entry.Action,
            entry.UserId);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync())
            {
                await WriteAuditAsync(entry);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task WriteAuditAsync(ProjectContentAuditEntry entry)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.LogAsync(
                entry.Action,
                entry.Message,
                userId: entry.UserId,
                userName: entry.UserDisplay,
                data: entry.Data);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Project-content audit delivery failed. Action={Action}, UserId={UserId}",
                entry.Action,
                entry.UserId);
        }
    }
}
