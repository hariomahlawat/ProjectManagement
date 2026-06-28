using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class FaceModelReadinessService : IFaceModelReadinessService
{
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IServiceScopeFactory _scopeFactory;

    public FaceModelReadinessService(
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _environment = environment;
        _scopeFactory = scopeFactory;
    }

    public async Task<FaceModelReadiness> CheckAsync(CancellationToken cancellationToken)
    {
        var people = _options.People;
        var checks = new List<FaceReadinessCheck>();
        var root = Path.IsPathRooted(people.ModelRoot)
            ? people.ModelRoot
            : Path.Combine(_environment.ContentRootPath, people.ModelRoot);
        var detectorPath = Path.Combine(root, people.Detector.FileName ?? string.Empty);
        var embedderPath = Path.Combine(root, people.Embedder.FileName ?? string.Empty);

        checks.Add(new("feature", "Feature configuration", people.Enabled,
            people.Enabled ? "Enabled" : "Disabled", "Set MediaLibrary:People:Enabled to true after model approval."));
        checks.Add(new("worker", "Face worker", people.WorkerEnabled,
            people.WorkerEnabled ? "Enabled" : "Disabled", "Enable the worker only after all readiness checks pass."));

        var detectorConfigured = IsModelConfigured(people.Detector);
        var embedderConfigured = IsModelConfigured(people.Embedder);
        checks.Add(new("detector-config", "Detector model", detectorConfigured,
            detectorConfigured ? $"Configured ({people.Detector.Key} {people.Detector.Version})" : "Not configured",
            "Provide the approved detector file, tensor contract, version and checksum."));
        checks.Add(new("embedder-config", "Embedding model", embedderConfigured,
            embedderConfigured ? $"Configured ({people.Embedder.Key} {people.Embedder.Version})" : "Not configured",
            "Provide the approved embedding file, tensor contract, version and checksum."));

        var detectorExists = detectorConfigured && File.Exists(detectorPath);
        var embedderExists = embedderConfigured && File.Exists(embedderPath);
        checks.Add(new("detector-file", "Detector model file", detectorExists,
            detectorExists ? "Installed" : "Missing", $"Install the approved model under {root}."));
        checks.Add(new("embedder-file", "Embedding model file", embedderExists,
            embedderExists ? "Installed" : "Missing", $"Install the approved model under {root}."));

        var licencesPresent = !string.IsNullOrWhiteSpace(people.Detector.License)
                              && !string.IsNullOrWhiteSpace(people.Embedder.License)
                              && !string.IsNullOrWhiteSpace(people.Detector.SourceUrl)
                              && !string.IsNullOrWhiteSpace(people.Embedder.SourceUrl);
        checks.Add(new("licence", "Model licence metadata", licencesPresent,
            licencesPresent ? "Recorded" : "Incomplete", "Record the model-weight licence and authoritative source URL."));

        var detectorChecksum = detectorExists && await MatchesAsync(detectorPath, people.Detector.Sha256, cancellationToken);
        var embedderChecksum = embedderExists && await MatchesAsync(embedderPath, people.Embedder.Sha256, cancellationToken);
        checks.Add(new("detector-checksum", "Detector checksum", detectorChecksum,
            detectorChecksum ? "Verified" : detectorExists ? "Mismatch or missing" : "Not checked"));
        checks.Add(new("embedder-checksum", "Embedding checksum", embedderChecksum,
            embedderChecksum ? "Verified" : embedderExists ? "Mismatch or missing" : "Not checked"));

        var runtimeAvailable = typeof(InferenceSession).Assembly.GetName().Version is not null;
        checks.Add(new("runtime", "ONNX Runtime", runtimeAvailable,
            runtimeAvailable ? $"Available ({typeof(InferenceSession).Assembly.GetName().Version})" : "Unavailable"));

        var schemaReady = await IsSchemaReadyAsync(cancellationToken);
        checks.Add(new("schema", "Database schema", schemaReady,
            schemaReady ? "Ready" : "Unavailable or migration pending", "Apply MediaLibraryDbContext migrations."));

        var cacheReady = IsCacheWritable();
        checks.Add(new("cache", "Derivative cache", cacheReady,
            cacheReady ? "Writable" : "Not writable", "Correct the configured cache path and application permissions."));

        var state = ResolveState(people.Enabled, detectorConfigured, embedderConfigured, detectorExists, embedderExists,
            licencesPresent, detectorChecksum, embedderChecksum, runtimeAvailable, schemaReady, cacheReady);
        var ready = state == FaceReadinessState.Ready && people.WorkerEnabled;
        var message = ready
            ? "Face intelligence is ready for controlled processing."
            : state == FaceReadinessState.Ready
                ? "Models and infrastructure are ready; the face worker remains disabled."
                : Describe(state);

        return new FaceModelReadiness(people.Enabled, ready, state, message, detectorPath, embedderPath,
            DateTimeOffset.UtcNow, checks);
    }

    private async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            if (!await db.Database.CanConnectAsync(cancellationToken)) return false;
            _ = await db.Faces.AsNoTracking().Select(x => x.Id).Take(1).ToListAsync(cancellationToken);
            return true;
        }
        catch { return false; }
    }

    private bool IsCacheWritable()
    {
        try
        {
            var root = Path.IsPathRooted(_options.CacheRoot)
                ? _options.CacheRoot
                : Path.Combine(_environment.ContentRootPath, _options.CacheRoot);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".face-readiness-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static bool IsModelConfigured(FaceModelOptions model) =>
        !string.IsNullOrWhiteSpace(model.Key)
        && !string.IsNullOrWhiteSpace(model.Version)
        && !string.IsNullOrWhiteSpace(model.FileName)
        && !string.IsNullOrWhiteSpace(model.Sha256)
        && !string.IsNullOrWhiteSpace(model.InputName);

    private static FaceReadinessState ResolveState(bool enabled, bool detectorConfigured, bool embedderConfigured,
        bool detectorExists, bool embedderExists, bool licences, bool detectorChecksum, bool embedderChecksum,
        bool runtime, bool schema, bool cache)
    {
        if (!enabled) return FaceReadinessState.Disabled;
        if (!detectorConfigured || !embedderConfigured) return FaceReadinessState.ConfigurationIncomplete;
        if (!detectorExists || !embedderExists) return FaceReadinessState.ModelsMissing;
        if (!licences) return FaceReadinessState.LicenceUnverified;
        if (!detectorChecksum || !embedderChecksum) return FaceReadinessState.ChecksumMismatch;
        if (!runtime) return FaceReadinessState.RuntimeUnavailable;
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
        FaceReadinessState.SchemaUnavailable => "The face-intelligence schema is unavailable.",
        FaceReadinessState.CacheUnavailable => "The derivative cache is not writable.",
        _ => "Face intelligence is not ready."
    };

    private static async Task<bool> MatchesAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false;
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected.Replace("-", string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
