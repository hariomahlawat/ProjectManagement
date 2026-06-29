using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Validates approved local model provenance, exact model bytes and ONNX contracts.
/// Full People readiness additionally validates the embedder, schema and protected cache.
/// Detector-only readiness is intentionally independent so classification assistance can
/// operate while identity processing remains disabled.
/// </summary>
public sealed class FaceModelReadinessService : IFaceModelReadinessService, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FaceModelReadinessService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FaceModelReadiness? _cached;
    private FaceDetectorReadiness? _detectorCached;
    private DateTimeOffset _cacheExpiresAtUtc;
    private DateTimeOffset _detectorCacheExpiresAtUtc;
    private bool _disposed;

    public FaceModelReadinessService(
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment,
        IServiceScopeFactory scopeFactory,
        ILogger<FaceModelReadinessService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<FaceModelReadiness> CheckAsync(CancellationToken cancellationToken)
        => CheckAsync(forceRefresh: false, cancellationToken);

    public async Task<FaceModelReadiness> CheckAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!forceRefresh && _cached is not null && DateTimeOffset.UtcNow < _cacheExpiresAtUtc)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cached is not null && DateTimeOffset.UtcNow < _cacheExpiresAtUtc)
            {
                return _cached;
            }

            _cached = await EvaluateFullAsync(cancellationToken);
            _cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<FaceDetectorReadiness> CheckDetectorAsync(CancellationToken cancellationToken)
        => CheckDetectorAsync(forceRefresh: false, cancellationToken);

    public async Task<FaceDetectorReadiness> CheckDetectorAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!forceRefresh
            && _detectorCached is not null
            && DateTimeOffset.UtcNow < _detectorCacheExpiresAtUtc)
        {
            return _detectorCached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && _detectorCached is not null
                && DateTimeOffset.UtcNow < _detectorCacheExpiresAtUtc)
            {
                return _detectorCached;
            }

            _detectorCached = await EvaluateDetectorAsync(cancellationToken);
            _detectorCacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _detectorCached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FaceDetectorReadiness> EvaluateDetectorAsync(CancellationToken cancellationToken)
    {
        var classification = _options.Classification;
        var people = _options.People;
        var checks = new List<FaceReadinessCheck>();
        var root = ResolveModelRoot(people.ModelRoot);
        var detectorPath = SafeModelPath(root, people.Detector.FileName);
        var assistanceEnabled = classification.Enabled && classification.FacePresenceAssistanceEnabled;

        checks.Add(new FaceReadinessCheck(
            "classification-assistance",
            "Classification face assistance",
            assistanceEnabled,
            assistanceEnabled ? "Enabled" : "Disabled",
            "Enable Classification:FacePresenceAssistanceEnabled only after detector approval."));

        var configured = IsModelConfigured(people.Detector);
        checks.Add(new FaceReadinessCheck(
            "detector-config",
            "Detector model",
            configured,
            configured
                ? $"Configured ({people.Detector.Key} {people.Detector.Version}; {people.Detector.Adapter})"
                : "Not configured",
            "Provide the approved detector file, adapter, tensor contract, version and checksum."));

        var exists = configured && detectorPath is not null && File.Exists(detectorPath);
        checks.Add(new FaceReadinessCheck(
            "detector-file",
            "Detector model file",
            exists,
            exists ? "Installed" : "Missing",
            $"Install the approved detector under {root}."));

        var provenance = HasApprovedProvenance(people.Detector);
        checks.Add(new FaceReadinessCheck(
            "detector-provenance",
            "Detector licence and provenance",
            provenance,
            provenance ? "Recorded" : "Incomplete",
            "Record the licence and either an authoritative source URL or approved offline artifact metadata."));

        var checksum = exists
                       && await MatchesAsync(detectorPath!, people.Detector.Sha256, cancellationToken);
        checks.Add(new FaceReadinessCheck(
            "detector-checksum",
            "Detector checksum",
            checksum,
            checksum ? "Verified" : exists ? "Mismatch or missing" : "Not checked"));

        var runtime = typeof(InferenceSession).Assembly.GetName().Version is not null;
        checks.Add(new FaceReadinessCheck(
            "runtime",
            "ONNX Runtime",
            runtime,
            runtime
                ? $"Available ({typeof(InferenceSession).Assembly.GetName().Version})"
                : "Unavailable"));

        var contractValid = false;
        var contractStatus = "Not checked";
        if (runtime && checksum)
        {
            (contractValid, contractStatus) = ValidateDetectorContract(detectorPath!, people.Detector);
        }
        checks.Add(new FaceReadinessCheck(
            "detector-contract",
            "Detector ONNX contract",
            contractValid,
            contractStatus,
            "Use only the pinned detector export or an explicitly supported adapter."));

        var state = ResolveDetectorState(
            assistanceEnabled,
            configured,
            exists,
            provenance,
            checksum,
            runtime,
            contractValid);
        var ready = state == FaceReadinessState.Ready;
        return new FaceDetectorReadiness(
            assistanceEnabled,
            ready,
            state,
            ready
                ? "Detector-only photograph assistance is ready. Identity processing remains independent."
                : DescribeDetector(state),
            detectorPath,
            DateTimeOffset.UtcNow,
            checks);
    }

    private async Task<FaceModelReadiness> EvaluateFullAsync(CancellationToken cancellationToken)
    {
        var people = _options.People;
        var checks = new List<FaceReadinessCheck>();
        var root = ResolveModelRoot(people.ModelRoot);
        var detectorPath = SafeModelPath(root, people.Detector.FileName);
        var embedderPath = SafeModelPath(root, people.Embedder.FileName);

        checks.Add(new FaceReadinessCheck(
            "feature",
            "Feature configuration",
            people.Enabled,
            people.Enabled ? "Enabled" : "Disabled",
            "Set MediaLibrary:People:Enabled to true only after model and privacy approval."));
        checks.Add(new FaceReadinessCheck(
            "worker",
            "Face worker",
            people.WorkerEnabled,
            people.WorkerEnabled ? "Enabled" : "Disabled",
            "Enable the worker only after all readiness checks pass."));

        var detectorConfigured = IsModelConfigured(people.Detector);
        var embedderConfigured = IsModelConfigured(people.Embedder);
        checks.Add(new FaceReadinessCheck(
            "detector-config",
            "Detector model",
            detectorConfigured,
            detectorConfigured
                ? $"Configured ({people.Detector.Key} {people.Detector.Version}; {people.Detector.Adapter})"
                : "Not configured",
            "Provide the approved detector file, adapter, tensor contract, version and checksum."));
        checks.Add(new FaceReadinessCheck(
            "embedder-config",
            "Embedding model",
            embedderConfigured,
            embedderConfigured
                ? $"Configured ({people.Embedder.Key} {people.Embedder.Version}; {people.Embedder.Adapter})"
                : "Not configured",
            "Provide the approved embedding file, adapter, tensor contract, version and checksum."));

        var detectorExists = detectorConfigured && detectorPath is not null && File.Exists(detectorPath);
        var embedderExists = embedderConfigured && embedderPath is not null && File.Exists(embedderPath);
        checks.Add(new FaceReadinessCheck(
            "detector-file",
            "Detector model file",
            detectorExists,
            detectorExists ? "Installed" : "Missing",
            $"Install the approved model under {root}."));
        checks.Add(new FaceReadinessCheck(
            "embedder-file",
            "Embedding model file",
            embedderExists,
            embedderExists ? "Installed" : "Missing",
            $"Install the approved model under {root}."));

        var provenancePresent = HasApprovedProvenance(people.Detector)
                                && HasApprovedProvenance(people.Embedder);
        checks.Add(new FaceReadinessCheck(
            "licence",
            "Model licence and provenance",
            provenancePresent,
            provenancePresent ? "Recorded" : "Incomplete",
            "Record each licence and either an authoritative source URL or approved offline artifact metadata."));

        var detectorChecksum = detectorExists
                               && await MatchesAsync(detectorPath!, people.Detector.Sha256, cancellationToken);
        var embedderChecksum = embedderExists
                               && await MatchesAsync(embedderPath!, people.Embedder.Sha256, cancellationToken);
        checks.Add(new FaceReadinessCheck(
            "detector-checksum",
            "Detector checksum",
            detectorChecksum,
            detectorChecksum ? "Verified" : detectorExists ? "Mismatch or missing" : "Not checked"));
        checks.Add(new FaceReadinessCheck(
            "embedder-checksum",
            "Embedding checksum",
            embedderChecksum,
            embedderChecksum ? "Verified" : embedderExists ? "Mismatch or missing" : "Not checked"));

        var runtimeAvailable = typeof(InferenceSession).Assembly.GetName().Version is not null;
        checks.Add(new FaceReadinessCheck(
            "runtime",
            "ONNX Runtime",
            runtimeAvailable,
            runtimeAvailable
                ? $"Available ({typeof(InferenceSession).Assembly.GetName().Version})"
                : "Unavailable"));

        var modelContractValid = false;
        var modelContractStatus = "Not checked";
        if (runtimeAvailable && detectorChecksum && embedderChecksum)
        {
            (modelContractValid, modelContractStatus) = ValidateModelContracts(
                detectorPath!,
                people.Detector,
                embedderPath!,
                people.Embedder);
        }
        checks.Add(new FaceReadinessCheck(
            "model-contract",
            "ONNX model contract",
            modelContractValid,
            modelContractStatus,
            "Use only the pinned detector/embedder exports or an explicitly supported adapter."));

        var schemaReady = await IsSchemaReadyAsync(cancellationToken);
        checks.Add(new FaceReadinessCheck(
            "schema",
            "Database schema",
            schemaReady,
            schemaReady ? "Ready" : "Unavailable or migration pending",
            "Apply MediaLibraryDbContext migrations."));

        var cacheReady = IsCacheWritable();
        checks.Add(new FaceReadinessCheck(
            "cache",
            "Derivative cache",
            cacheReady,
            cacheReady ? "Writable" : "Not writable",
            "Correct the configured cache path and application permissions."));

        var state = ResolveState(
            people.Enabled,
            detectorConfigured,
            embedderConfigured,
            detectorExists,
            embedderExists,
            provenancePresent,
            detectorChecksum,
            embedderChecksum,
            runtimeAvailable,
            modelContractValid,
            schemaReady,
            cacheReady);
        var ready = state == FaceReadinessState.Ready && people.WorkerEnabled;
        var message = ready
            ? "Face intelligence is ready for controlled processing."
            : state == FaceReadinessState.Ready
                ? "Models and infrastructure are ready; the face worker remains disabled."
                : Describe(state);

        return new FaceModelReadiness(
            people.Enabled,
            ready,
            state,
            message,
            detectorPath,
            embedderPath,
            DateTimeOffset.UtcNow,
            checks);
    }

    private (bool IsValid, string Status) ValidateModelContracts(
        string detectorPath,
        FaceModelOptions detector,
        string embedderPath,
        FaceModelOptions embedder)
    {
        var detectorResult = ValidateDetectorContract(detectorPath, detector);
        if (!detectorResult.IsValid)
        {
            return detectorResult;
        }

        try
        {
            using var embedderSession = new InferenceSession(embedderPath);
            ValidateInput(embedderSession, embedder);
            if (!string.IsNullOrWhiteSpace(embedder.EmbeddingOutputName))
            {
                RequireOutput(embedderSession, embedder.EmbeddingOutputName, "embedding");
            }
            else if (embedderSession.OutputMetadata.Count != 1)
            {
                throw new InvalidDataException(
                    "The embedder exposes multiple outputs; EmbeddingOutputName must be configured.");
            }

            return (true, "Validated");
        }
        catch (Exception exception) when (IsModelValidationException(exception))
        {
            _logger.LogWarning(exception, "Face embedding model contract validation failed.");
            return (false, exception.Message);
        }
    }

    private (bool IsValid, string Status) ValidateDetectorContract(
        string detectorPath,
        FaceModelOptions detector)
    {
        try
        {
            using var detectorSession = new InferenceSession(detectorPath);
            ValidateInput(detectorSession, detector);
            if (detector.Adapter.Equals("YuNet", StringComparison.OrdinalIgnoreCase))
            {
                var required = new[]
                {
                    "cls_8", "cls_16", "cls_32",
                    "obj_8", "obj_16", "obj_32",
                    "bbox_8", "bbox_16", "bbox_32",
                    "kps_8", "kps_16", "kps_32"
                };
                var missing = required
                    .Where(name => !detectorSession.OutputMetadata.ContainsKey(name))
                    .ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidDataException(
                        $"YuNet outputs missing: {string.Join(", ", missing)}.");
                }
            }
            else
            {
                RequireOutput(detectorSession, detector.BoxesOutputName, "boxes");
                RequireOutput(detectorSession, detector.ScoresOutputName, "scores");
            }

            return (true, "Validated");
        }
        catch (Exception exception) when (IsModelValidationException(exception))
        {
            _logger.LogWarning(exception, "Face detector model contract validation failed.");
            return (false, exception.Message);
        }
    }

    private static bool IsModelValidationException(Exception exception)
        => exception is OnnxRuntimeException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException;

    private static void ValidateInput(InferenceSession session, FaceModelOptions model)
    {
        if (!string.IsNullOrWhiteSpace(model.InputName)
            && session.InputMetadata.ContainsKey(model.InputName))
        {
            return;
        }

        if (session.InputMetadata.Count == 1)
        {
            return;
        }

        throw new InvalidDataException(
            $"Input '{model.InputName}' was not found for {model.Key}, and the model exposes multiple inputs.");
    }

    private static void RequireOutput(InferenceSession session, string name, string label)
    {
        if (string.IsNullOrWhiteSpace(name) || !session.OutputMetadata.ContainsKey(name))
        {
            throw new InvalidDataException($"Configured {label} output '{name}' was not found.");
        }
    }

    private async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return false;
            }

            _ = await db.Faces.AsNoTracking()
                .Select(face => new { face.Id, face.ConcurrencyToken })
                .Take(1)
                .ToListAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug(exception, "Face schema readiness check failed.");
            return false;
        }
    }

    private bool IsCacheWritable()
    {
        try
        {
            var root = Path.GetFullPath(Path.IsPathRooted(_options.CacheRoot)
                ? _options.CacheRoot
                : Path.Combine(_environment.ContentRootPath, _options.CacheRoot));
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".face-readiness-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or System.Security.SecurityException)
        {
            _logger.LogDebug(exception, "Face cache readiness check failed.");
            return false;
        }
    }

    private string ResolveModelRoot(string modelRoot)
        => Path.GetFullPath(Path.IsPathRooted(modelRoot)
            ? modelRoot
            : Path.Combine(_environment.ContentRootPath, modelRoot));

    private static string? SafeModelPath(string root, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, fileName));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(
            rootPrefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? fullPath
            : null;
    }

    private static bool IsModelConfigured(FaceModelOptions model)
        => !string.IsNullOrWhiteSpace(model.Key)
           && !string.IsNullOrWhiteSpace(model.Version)
           && !string.IsNullOrWhiteSpace(model.Adapter)
           && !string.IsNullOrWhiteSpace(model.FileName)
           && !string.IsNullOrWhiteSpace(model.Sha256)
           && !string.IsNullOrWhiteSpace(model.InputName);

    private static bool HasApprovedProvenance(FaceModelOptions model)
    {
        if (string.IsNullOrWhiteSpace(model.License))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(model.SourceUrl))
        {
            return Uri.TryCreate(model.SourceUrl, UriKind.Absolute, out var sourceUri)
                   && (string.Equals(sourceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        return !string.IsNullOrWhiteSpace(model.Publisher)
               && !string.IsNullOrWhiteSpace(model.ApprovedArtifactId)
               && DateOnly.TryParseExact(
                   model.AcquiredOn,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out _);
    }

    private static FaceReadinessState ResolveDetectorState(
        bool enabled,
        bool configured,
        bool exists,
        bool provenance,
        bool checksum,
        bool runtime,
        bool contract)
    {
        if (!enabled) return FaceReadinessState.Disabled;
        if (!configured) return FaceReadinessState.ConfigurationIncomplete;
        if (!exists) return FaceReadinessState.ModelsMissing;
        if (!provenance) return FaceReadinessState.LicenceUnverified;
        if (!checksum) return FaceReadinessState.ChecksumMismatch;
        if (!runtime) return FaceReadinessState.RuntimeUnavailable;
        if (!contract) return FaceReadinessState.ModelContractInvalid;
        return FaceReadinessState.Ready;
    }

    private static FaceReadinessState ResolveState(
        bool enabled,
        bool detectorConfigured,
        bool embedderConfigured,
        bool detectorExists,
        bool embedderExists,
        bool provenance,
        bool detectorChecksum,
        bool embedderChecksum,
        bool runtime,
        bool modelContract,
        bool schema,
        bool cache)
    {
        if (!enabled) return FaceReadinessState.Disabled;
        if (!detectorConfigured || !embedderConfigured) return FaceReadinessState.ConfigurationIncomplete;
        if (!detectorExists || !embedderExists) return FaceReadinessState.ModelsMissing;
        if (!provenance) return FaceReadinessState.LicenceUnverified;
        if (!detectorChecksum || !embedderChecksum) return FaceReadinessState.ChecksumMismatch;
        if (!runtime) return FaceReadinessState.RuntimeUnavailable;
        if (!modelContract) return FaceReadinessState.ModelContractInvalid;
        if (!schema) return FaceReadinessState.SchemaUnavailable;
        if (!cache) return FaceReadinessState.CacheUnavailable;
        return FaceReadinessState.Ready;
    }

    private static string DescribeDetector(FaceReadinessState state)
        => state switch
        {
            FaceReadinessState.Disabled => "Detector-only classification assistance is disabled.",
            FaceReadinessState.ConfigurationIncomplete => "The approved detector configuration is incomplete.",
            FaceReadinessState.ModelsMissing => "The approved detector file is missing.",
            FaceReadinessState.ChecksumMismatch => "The detector checksum is missing or does not match the approved value.",
            FaceReadinessState.LicenceUnverified => "Detector licence or provenance metadata is incomplete.",
            FaceReadinessState.RuntimeUnavailable => "ONNX Runtime is unavailable.",
            FaceReadinessState.ModelContractInvalid => "The installed detector does not match the approved ONNX contract.",
            _ => "Detector-only classification assistance is not ready."
        };

    private static string Describe(FaceReadinessState state)
        => state switch
        {
            FaceReadinessState.Disabled => "Face intelligence is disabled.",
            FaceReadinessState.ConfigurationIncomplete => "Approved model configuration is incomplete.",
            FaceReadinessState.ModelsMissing => "One or more approved model files are missing.",
            FaceReadinessState.ChecksumMismatch => "A model checksum is missing or does not match the approved value.",
            FaceReadinessState.LicenceUnverified => "Model licence or provenance metadata is incomplete.",
            FaceReadinessState.RuntimeUnavailable => "ONNX Runtime is unavailable.",
            FaceReadinessState.ModelContractInvalid => "An installed ONNX model does not match the approved tensor contract.",
            FaceReadinessState.SchemaUnavailable => "The face-intelligence schema is unavailable.",
            FaceReadinessState.CacheUnavailable => "The derivative cache is not writable.",
            _ => "Face intelligence is not ready."
        };

    private static async Task<bool> MatchesAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexString(hash),
            expected.Replace("-", string.Empty, StringComparison.Ordinal).Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FaceModelReadinessService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
