using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Validates feature configuration, model provenance, exact model bytes, ONNX tensor
/// contracts, schema availability and cache permissions. Results are briefly cached so
/// workers do not hash and load large model files for every image.
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
    private DateTimeOffset _cacheExpiresAtUtc;
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

    public async Task<FaceModelReadiness> CheckAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FaceModelReadinessService));
        }
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

            _cached = await EvaluateAsync(cancellationToken);
            _cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FaceModelReadiness> EvaluateAsync(CancellationToken cancellationToken)
    {
        var people = _options.People;
        var checks = new List<FaceReadinessCheck>();
        var root = Path.GetFullPath(Path.IsPathRooted(people.ModelRoot)
            ? people.ModelRoot
            : Path.Combine(_environment.ContentRootPath, people.ModelRoot));
        var detectorPath = SafeModelPath(root, people.Detector.FileName);
        var embedderPath = SafeModelPath(root, people.Embedder.FileName);

        checks.Add(new("feature", "Feature configuration", people.Enabled,
            people.Enabled ? "Enabled" : "Disabled",
            "Set MediaLibrary:People:Enabled to true only after model and privacy approval."));
        checks.Add(new("worker", "Face worker", people.WorkerEnabled,
            people.WorkerEnabled ? "Enabled" : "Disabled",
            "Enable the worker only after all readiness checks pass."));

        var detectorConfigured = IsModelConfigured(people.Detector);
        var embedderConfigured = IsModelConfigured(people.Embedder);
        checks.Add(new("detector-config", "Detector model", detectorConfigured,
            detectorConfigured ? $"Configured ({people.Detector.Key} {people.Detector.Version}; {people.Detector.Adapter})" : "Not configured",
            "Provide the approved detector file, adapter, tensor contract, version and checksum."));
        checks.Add(new("embedder-config", "Embedding model", embedderConfigured,
            embedderConfigured ? $"Configured ({people.Embedder.Key} {people.Embedder.Version}; {people.Embedder.Adapter})" : "Not configured",
            "Provide the approved embedding file, adapter, tensor contract, version and checksum."));

        var detectorExists = detectorConfigured && detectorPath is not null && File.Exists(detectorPath);
        var embedderExists = embedderConfigured && embedderPath is not null && File.Exists(embedderPath);
        checks.Add(new("detector-file", "Detector model file", detectorExists,
            detectorExists ? "Installed" : "Missing", $"Install the approved model under {root}."));
        checks.Add(new("embedder-file", "Embedding model file", embedderExists,
            embedderExists ? "Installed" : "Missing", $"Install the approved model under {root}."));

        var licencesPresent = HasLicenceMetadata(people.Detector) && HasLicenceMetadata(people.Embedder);
        checks.Add(new("licence", "Model licence metadata", licencesPresent,
            licencesPresent ? "Recorded" : "Incomplete",
            "Record the exact model-weight licence and authoritative source URL."));

        var detectorChecksum = detectorExists
                               && await MatchesAsync(detectorPath!, people.Detector.Sha256, cancellationToken);
        var embedderChecksum = embedderExists
                               && await MatchesAsync(embedderPath!, people.Embedder.Sha256, cancellationToken);
        checks.Add(new("detector-checksum", "Detector checksum", detectorChecksum,
            detectorChecksum ? "Verified" : detectorExists ? "Mismatch or missing" : "Not checked"));
        checks.Add(new("embedder-checksum", "Embedding checksum", embedderChecksum,
            embedderChecksum ? "Verified" : embedderExists ? "Mismatch or missing" : "Not checked"));

        var runtimeAvailable = typeof(InferenceSession).Assembly.GetName().Version is not null;
        checks.Add(new("runtime", "ONNX Runtime", runtimeAvailable,
            runtimeAvailable ? $"Available ({typeof(InferenceSession).Assembly.GetName().Version})" : "Unavailable"));

        var modelContractValid = false;
        var modelContractStatus = "Not checked";
        if (runtimeAvailable && detectorChecksum && embedderChecksum)
        {
            (modelContractValid, modelContractStatus) = ValidateModelContracts(
                detectorPath!, people.Detector,
                embedderPath!, people.Embedder);
        }
        checks.Add(new("model-contract", "ONNX model contract", modelContractValid,
            modelContractStatus,
            "Use only the pinned YuNet/SFace exports or provide an explicitly supported adapter."));

        var schemaReady = await IsSchemaReadyAsync(cancellationToken);
        checks.Add(new("schema", "Database schema", schemaReady,
            schemaReady ? "Ready" : "Unavailable or migration pending",
            "Apply MediaLibraryDbContext migrations."));

        var cacheReady = IsCacheWritable();
        checks.Add(new("cache", "Derivative cache", cacheReady,
            cacheReady ? "Writable" : "Not writable",
            "Correct the configured cache path and application permissions."));

        var state = ResolveState(
            people.Enabled,
            detectorConfigured,
            embedderConfigured,
            detectorExists,
            embedderExists,
            licencesPresent,
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
                var missing = required.Where(name => !detectorSession.OutputMetadata.ContainsKey(name)).ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidDataException($"YuNet outputs missing: {string.Join(", ", missing)}.");
                }
            }
            else
            {
                RequireOutput(detectorSession, detector.BoxesOutputName, "boxes");
                RequireOutput(detectorSession, detector.ScoresOutputName, "scores");
            }

            using var embedderSession = new InferenceSession(embedderPath);
            ValidateInput(embedderSession, embedder);
            if (!string.IsNullOrWhiteSpace(embedder.EmbeddingOutputName))
            {
                RequireOutput(embedderSession, embedder.EmbeddingOutputName, "embedding");
            }
            else if (embedderSession.OutputMetadata.Count != 1)
            {
                throw new InvalidDataException("The embedder exposes multiple outputs; EmbeddingOutputName must be configured.");
            }

            return (true, "Validated");
        }
        catch (Exception exception) when (exception is OnnxRuntimeException
                                           or InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Face model contract validation failed.");
            return (false, exception.Message);
        }
    }

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

    private static string? SafeModelPath(string root, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, fileName));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPrefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
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

    private static bool HasLicenceMetadata(FaceModelOptions model)
        => !string.IsNullOrWhiteSpace(model.License)
           && !string.IsNullOrWhiteSpace(model.SourceUrl);

    private static FaceReadinessState ResolveState(
        bool enabled,
        bool detectorConfigured,
        bool embedderConfigured,
        bool detectorExists,
        bool embedderExists,
        bool licences,
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
        if (!licences) return FaceReadinessState.LicenceUnverified;
        if (!detectorChecksum || !embedderChecksum) return FaceReadinessState.ChecksumMismatch;
        if (!runtime) return FaceReadinessState.RuntimeUnavailable;
        if (!modelContract) return FaceReadinessState.ModelContractInvalid;
        if (!schema) return FaceReadinessState.SchemaUnavailable;
        if (!cache) return FaceReadinessState.CacheUnavailable;
        return FaceReadinessState.Ready;
    }

    private static string Describe(FaceReadinessState state) => state switch
    {
        FaceReadinessState.Disabled => "Face intelligence is disabled.",
        FaceReadinessState.ConfigurationIncomplete => "Approved model configuration is incomplete.",
        FaceReadinessState.ModelsMissing => "One or more approved model files are missing.",
        FaceReadinessState.ChecksumMismatch => "A model checksum is missing or does not match the approved value.",
        FaceReadinessState.LicenceUnverified => "Model licence metadata is incomplete.",
        FaceReadinessState.RuntimeUnavailable => "ONNX Runtime is unavailable.",
        FaceReadinessState.ModelContractInvalid => "The installed ONNX model does not match the approved tensor contract.",
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
