using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Bridges committed PRISM-owned media changes into the durable Photos catalogue.
/// Source-domain operations remain authoritative: a catalogue outage is reported and
/// reconciled later, but never rolls back an otherwise successful Activity operation.
/// </summary>
public sealed class PrismMediaIngestionCoordinator : IPrismMediaIngestionCoordinator
{
    private readonly IMediaLibrarySchemaService _schema;
    private readonly IMediaSourceBootstrapper _bootstrapper;
    private readonly IPrismMediaCatalogueSynchronizer _synchronizer;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<PrismMediaIngestionCoordinator> _logger;

    public PrismMediaIngestionCoordinator(
        IMediaLibrarySchemaService schema,
        IMediaSourceBootstrapper bootstrapper,
        IPrismMediaCatalogueSynchronizer synchronizer,
        IOptions<MediaLibraryOptions> options,
        ILogger<PrismMediaIngestionCoordinator> logger)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PrismMediaIngestionResult> ReconcileAfterSourceChangeAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (!_options.IsCatalogueEnabled || !_options.Catalogue.SynchronizePrismMedia)
        {
            return new PrismMediaIngestionResult(false, "Disabled");
        }

        try
        {
            var schema = await _schema.GetStatusAsync(cancellationToken);
            if (!schema.IsAvailable || !schema.IsOperational)
            {
                _logger.LogWarning(
                    "Deferred PRISM media ingestion after {Reason}: catalogue schema is not operational. Reference={Reference}",
                    reason,
                    schema.DiagnosticReference);
                return new PrismMediaIngestionResult(false, "Schema unavailable", schema.Error);
            }

            await _bootstrapper.EnsureConfiguredSourcesAsync(cancellationToken);
            await _synchronizer.SynchronizeAsync(cancellationToken);

            _logger.LogInformation("PRISM media catalogue reconciled after {Reason}", reason);
            return new PrismMediaIngestionResult(true, "Synchronized");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            _logger.LogError(ex, "Deferred PRISM media ingestion failed after {Reason}", reason);
            return new PrismMediaIngestionResult(false, "Failed", error);
        }
    }
}

/// <summary>
/// Serializes catalogue reconciliation across the scanner, upload-driven ingestion and
/// administrative reconciliation actions within this application instance.
/// </summary>
public sealed class PrismMediaSynchronizationGate : IPrismMediaSynchronizationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
