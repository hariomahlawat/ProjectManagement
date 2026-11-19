using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.Chat;

namespace ProjectManagement.Hosted;

/// <summary>
/// Re-checks open chat connections on the same interval as the sign-in cookie
/// (<c>Security:SessionRevalidationMinutes</c>, one minute by default), so disabling an account
/// reaches its open chat tabs as promptly as the rest of PRISM (<see cref="ChatConnectionRevalidator"/>).
/// One query per sweep, and none while no chat tab is open. A failed sweep is retried on the
/// next one.
/// </summary>
public sealed class ChatConnectionRevalidationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChatConnectionRegistry _registry;
    private readonly IOptionsMonitor<ChatOptions> _options;
    private readonly ILogger<ChatConnectionRevalidationWorker> _logger;
    private readonly TimeSpan _interval;

    public ChatConnectionRevalidationWorker(
        IServiceScopeFactory scopeFactory,
        ChatConnectionRegistry registry,
        IOptionsMonitor<ChatOptions> options,
        IConfiguration configuration,
        ILogger<ChatConnectionRevalidationWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = SessionRevalidation.ResolveInterval(configuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_registry.Count == 0)
                {
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ChatConnectionRevalidator>().RevalidateAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Re-checking open chat connections failed; it runs again shortly.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
