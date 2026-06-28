using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.MediaLibrary.Options;

/// <summary>
/// Validates only capabilities that are enabled. Optional external folders and future
/// People intelligence must never prevent the core PRISM application from starting
/// merely because they are absent or disabled.
/// </summary>
public sealed class MediaLibraryOptionsValidator : IValidateOptions<MediaLibraryOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (!options.Catalogue.Enabled)
        {
            if (options.ExternalSources.Enabled)
            {
                failures.Add("MediaLibrary:ExternalSources cannot be enabled while Catalogue:Enabled is false.");
            }

            if (options.Processing.WorkerEnabled)
            {
                failures.Add("MediaLibrary:Processing:WorkerEnabled cannot be true while Catalogue:Enabled is false.");
            }

            if (options.People.Enabled || options.People.WorkerEnabled)
            {
                failures.Add("MediaLibrary:People cannot be enabled while Catalogue:Enabled is false.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        if ((options.IsExternalSourceFeatureEnabled || options.IsProcessingWorkerEnabled)
            && string.IsNullOrWhiteSpace(options.CacheRoot))
        {
            failures.Add("MediaLibrary:CacheRoot is required while external media or processing is enabled.");
        }

        if (options.Catalogue.SynchronizePrismMedia
            && options.Catalogue.SynchronizeIntervalMinutes is < 1 or > 10080)
        {
            failures.Add("MediaLibrary:Catalogue:SynchronizeIntervalMinutes must be between 1 and 10080.");
        }

        if (options.IsExternalSourceFeatureEnabled)
        {
            ValidateExternalSourceOptions(options, failures);
        }

        if (options.IsProcessingWorkerEnabled)
        {
            ValidateProcessingOptions(options, failures);
        }

        if (options.People.WorkerEnabled && !options.People.Enabled)
        {
            failures.Add("MediaLibrary:People:WorkerEnabled cannot be true while People:Enabled is false.");
        }

        if (options.People.Enabled)
        {
            ValidatePeopleOptions(options.People, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateExternalSourceOptions(
        MediaLibraryOptions options,
        ICollection<string> failures)
    {
        if (options.ExternalSources.DefaultScanIntervalMinutes is < 1 or > 10080)
        {
            failures.Add("MediaLibrary:ExternalSources:DefaultScanIntervalMinutes must be between 1 and 10080.");
        }

        if (options.ExternalSources.ScanBatchSize is < 10 or > 5000)
        {
            failures.Add("MediaLibrary:ExternalSources:ScanBatchSize must be between 10 and 5000.");
        }

        if (options.ExternalSources.IdleDelaySeconds is < 1 or > 3600)
        {
            failures.Add("MediaLibrary:ExternalSources:IdleDelaySeconds must be between 1 and 3600.");
        }

        if (options.ExternalSources.ScanLeaseMinutes is < 2 or > 240)
        {
            failures.Add("MediaLibrary:ExternalSources:ScanLeaseMinutes must be between 2 and 240.");
        }

        // Disabled definitions are intentionally ignored. They may retain an old path
        // for audit or later reconnection without becoming a startup dependency.
        var enabledSources = options.GetBootstrapSources()
            .Where(source => source.Enabled)
            .ToList();

        var duplicateKeys = enabledSources
            .GroupBy(source => source.Key?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateKeys.Length > 0)
        {
            failures.Add($"MediaLibrary source keys must be unique: {string.Join(", ", duplicateKeys)}.");
        }

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var duplicatePaths = enabledSources
            .Select(source => TryNormalizeFullPath(source.RootPath))
            .Where(path => path is not null)
            .Select(path => path!)
            .GroupBy(path => path, pathComparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicatePaths.Length > 0)
        {
            failures.Add($"A file-system folder can be configured only once: {string.Join(", ", duplicatePaths)}.");
        }

        foreach (var source in enabledSources)
        {
            ValidateSource(source, failures);
        }
    }

    private static void ValidateProcessingOptions(
        MediaLibraryOptions options,
        ICollection<string> failures)
    {
        if (options.Processing.BatchSize is < 1 or > 16)
        {
            failures.Add("MediaLibrary:Processing:BatchSize must be between 1 and 16.");
        }

        if (options.Processing.IdleDelaySeconds is < 1 or > 3600)
        {
            failures.Add("MediaLibrary:Processing:IdleDelaySeconds must be between 1 and 3600.");
        }

        if (options.Processing.MaxAttempts is < 1 or > 20)
        {
            failures.Add("MediaLibrary:Processing:MaxAttempts must be between 1 and 20.");
        }

        if (options.Processing.MaxImageFileSizeBytes is < 1_048_576 or > 2_147_483_648)
        {
            failures.Add("MediaLibrary:Processing:MaxImageFileSizeBytes must be between 1 MB and 2 GB.");
        }

        if (options.Processing.ThumbnailMaxPixels is < 128 or > 2048)
        {
            failures.Add("MediaLibrary:Processing:ThumbnailMaxPixels must be between 128 and 2048.");
        }

        if (options.Processing.PreviewMaxPixels < options.Processing.ThumbnailMaxPixels
            || options.Processing.PreviewMaxPixels > 8192)
        {
            failures.Add("MediaLibrary:Processing:PreviewMaxPixels must be at least ThumbnailMaxPixels and no more than 8192.");
        }

        if (options.Processing.WebpQuality is < 40 or > 100)
        {
            failures.Add("MediaLibrary:Processing:WebpQuality must be between 40 and 100.");
        }
    }



    private static void ValidatePeopleOptions(MediaPeopleOptions people, ICollection<string> failures)
    {
        if (people.MaximumConcurrentAssets is < 1 or > 4)
            failures.Add("MediaLibrary:People:MaximumConcurrentAssets must be between 1 and 4 for CPU processing.");
        if (people.MaximumFacesPerAsset is < 1 or > 100)
            failures.Add("MediaLibrary:People:MaximumFacesPerAsset must be between 1 and 100.");
        if (people.MinimumFacePixels is < 32 or > 1024)
            failures.Add("MediaLibrary:People:MinimumFacePixels must be between 32 and 1024.");
        if (people.MinimumDetectionConfidence is < 0.1 or > 1)
            failures.Add("MediaLibrary:People:MinimumDetectionConfidence must be between 0.1 and 1.0.");
        if (people.MinimumQualityScore is < 0 or > 1)
            failures.Add("MediaLibrary:People:MinimumQualityScore must be between 0 and 1.");
        if (people.CandidateSimilarityThreshold is < -1 or > 1)
            failures.Add("MediaLibrary:People:CandidateSimilarityThreshold must be between -1 and 1.");
        if (string.IsNullOrWhiteSpace(people.ModelRoot))
            failures.Add("MediaLibrary:People:ModelRoot is required when face intelligence is enabled.");
        ValidateFaceModel(people.Detector, "Detector", failures);
        ValidateFaceModel(people.Embedder, "Embedder", failures);
    }

    private static void ValidateFaceModel(FaceModelOptions model, string label, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(model.Key)) failures.Add($"MediaLibrary:People:{label}:Key is required.");
        if (string.IsNullOrWhiteSpace(model.Version)) failures.Add($"MediaLibrary:People:{label}:Version is required.");
        if (string.IsNullOrWhiteSpace(model.FileName)) failures.Add($"MediaLibrary:People:{label}:FileName is required.");
        if (string.IsNullOrWhiteSpace(model.Sha256) || model.Sha256.Replace("-", string.Empty).Length != 64)
            failures.Add($"MediaLibrary:People:{label}:Sha256 must contain the approved 64-character SHA-256 checksum.");
        if (string.IsNullOrWhiteSpace(model.License)) failures.Add($"MediaLibrary:People:{label}:License is required.");
        if (string.IsNullOrWhiteSpace(model.SourceUrl)) failures.Add($"MediaLibrary:People:{label}:SourceUrl is required.");
        if (model.InputWidth is < 32 or > 4096 || model.InputHeight is < 32 or > 4096)
            failures.Add($"MediaLibrary:People:{label} input dimensions must be between 32 and 4096 pixels.");
    }

    private static string? TryNormalizeFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException
                                   or System.Security.SecurityException)
        {
            return null;
        }
    }

    public static void ValidateSource(MediaSourceOptions source, ICollection<string> failures)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(failures);

        if (string.IsNullOrWhiteSpace(source.Key))
        {
            failures.Add("Every enabled MediaLibrary source requires a Key.");
        }

        if (string.IsNullOrWhiteSpace(source.Name))
        {
            failures.Add($"MediaLibrary source '{source.Key}' requires a Name.");
        }

        // NetworkShare is accepted only as a backward-compatible configuration alias.
        if (!string.Equals(source.Type, "FileSystem", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Type, "NetworkShare", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"MediaLibrary source '{source.Key}' has unsupported Type '{source.Type}'. Use FileSystem.");
        }

        if (string.IsNullOrWhiteSpace(source.RootPath) || !Path.IsPathFullyQualified(source.RootPath))
        {
            failures.Add($"MediaLibrary source '{source.Key}' requires a fully-qualified local or UNC RootPath.");
        }
        else if (TryNormalizeFullPath(source.RootPath) is null)
        {
            failures.Add($"MediaLibrary source '{source.Key}' has an invalid RootPath.");
        }

        if (source.ScanIntervalMinutes.HasValue
            && source.ScanIntervalMinutes.Value is < 1 or > 10080)
        {
            failures.Add($"MediaLibrary source '{source.Key}' ScanIntervalMinutes must be between 1 and 10080.");
        }

        if (source.AllowedExtensions is null || source.AllowedExtensions.Count == 0)
        {
            failures.Add($"MediaLibrary source '{source.Key}' requires at least one allowed extension.");
        }
    }
}
