using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Optional controlled schema bootstrap. Production can keep AutoMigrate=false and
/// apply migrations through deployment or the protected administration action.
/// </summary>
public sealed class MediaLibrarySchemaInitializerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<MediaLibrarySchemaInitializerWorker> _logger;

    public MediaLibrarySchemaInitializerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaLibraryOptions> options,
        ILogger<MediaLibrarySchemaInitializerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.AutoMigrate)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var schema = scope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
        var result = await schema.MigrateAsync(stoppingToken);

        if (result.IsOperational)
        {
            _logger.LogInformation(result.IsCurrent ? "Media catalogue schema is current" : "Media catalogue is operational with a migration-history warning");
        }
        else
        {
            _logger.LogWarning("Media catalogue schema initialization did not complete: {Error}", result.Error);
        }
    }
}
