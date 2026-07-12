using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Compatibility readiness observer for older hosts that may still register this type.
/// Schema mutation is exclusively owned by the synchronous application startup gate;
/// hosted services must never run migrations after request processing can begin.
/// </summary>
[Obsolete(
    "Runtime schema initialization is prohibited. Use the synchronous DatabaseStartupMigrator deployment boundary.",
    error: false)]
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
        if (!_options.Enabled)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var schema = scope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
        var result = await schema.GetStatusAsync(stoppingToken);

        if (result.IsCurrent)
        {
            _logger.LogInformation(
                "Media catalogue readiness observer confirmed the schema is current. Reference={Reference}",
                result.DiagnosticReference);
            return;
        }

        _logger.LogCritical(
            "Media catalogue readiness observer found a non-current schema after startup. " +
            "Runtime migration is intentionally disabled; recycle the application only after deploying the complete migration assembly. " +
            "Reference={Reference}; Error={Error}",
            result.DiagnosticReference,
            result.Error);
    }
}
