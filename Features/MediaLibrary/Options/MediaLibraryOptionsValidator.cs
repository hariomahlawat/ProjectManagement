using System.Globalization;
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

        if ((options.IsExternalSourceFeatureEnabled
             || options.IsAnyProcessingWorkerEnabled
             || options.People.Enabled)
            && string.IsNullOrWhiteSpace(options.CacheRoot))
        {
            failures.Add("MediaLibrary:CacheRoot is required while external media or processing is enabled.");
        }

        if (options.Catalogue.SynchronizePrismMedia
            && options.Catalogue.SynchronizeIntervalSeconds > 0
            && options.Catalogue.SynchronizeIntervalSeconds is < 5 or > 3600)
        {
            failures.Add("MediaLibrary:Catalogue:SynchronizeIntervalSeconds must be between 5 and 3600.");
        }

        if (options.Catalogue.SynchronizePrismMedia
            && options.Catalogue.SynchronizeIntervalSeconds <= 0
            && options.Catalogue.SynchronizeIntervalMinutes is < 1 or > 10080)
        {
            failures.Add("MediaLibrary:Catalogue:SynchronizeIntervalMinutes must be between 1 and 10080 when the seconds setting is disabled.");
        }

        if (options.IsExternalSourceFeatureEnabled)
        {
            ValidateExternalSourceOptions(options, failures);
        }

        if (options.IsAnyProcessingWorkerEnabled)
        {
            ValidateProcessingOptions(options, failures);
        }

        ValidateClassificationOptions(options.Classification, failures);

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



    private static void ValidateClassificationOptions(MediaClassificationOptions classification, ICollection<string> failures)
    {
        static bool Invalid(double value) => value < 0 || value > 1 || double.IsNaN(value) || double.IsInfinity(value);
        if (Invalid(classification.MinimumConfidence)) failures.Add("MediaLibrary:Classification:MinimumConfidence must be between 0 and 1.");
        if (Invalid(classification.PhotographThreshold)) failures.Add("MediaLibrary:Classification:PhotographThreshold must be between 0 and 1.");
        if (Invalid(classification.NaturalPhotoAutoAcceptThreshold)) failures.Add("MediaLibrary:Classification:NaturalPhotoAutoAcceptThreshold must be between 0 and 1.");
        if (classification.NaturalPhotoAutoAcceptThreshold < classification.PhotographThreshold) failures.Add("MediaLibrary:Classification:NaturalPhotoAutoAcceptThreshold cannot be below PhotographThreshold.");
        if (Invalid(classification.MinimumScoreMargin)) failures.Add("MediaLibrary:Classification:MinimumScoreMargin must be between 0 and 1.");
        if (Invalid(classification.PhotographMinimumScoreMargin)) failures.Add("MediaLibrary:Classification:PhotographMinimumScoreMargin must be between 0 and 1.");
        if (classification.PhotographMinimumScoreMargin < classification.MinimumScoreMargin) failures.Add("MediaLibrary:Classification:PhotographMinimumScoreMargin cannot be below MinimumScoreMargin.");
        if (Invalid(classification.StrongConflictScore)) failures.Add("MediaLibrary:Classification:StrongConflictScore must be between 0 and 1.");
        if (Invalid(classification.FaceProbeBasePhotographMinimumScore)) failures.Add("MediaLibrary:Classification:FaceProbeBasePhotographMinimumScore must be between 0 and 1.");
        if (Invalid(classification.NaturalPhotoBaselineMinimumScore)) failures.Add("MediaLibrary:Classification:NaturalPhotoBaselineMinimumScore must be between 0 and 1.");
        if (Invalid(classification.BaseNonPhotoConflictScore)) failures.Add("MediaLibrary:Classification:BaseNonPhotoConflictScore must be between 0 and 1.");
        if (Invalid(classification.DocumentStructureVetoThreshold)) failures.Add("MediaLibrary:Classification:DocumentStructureVetoThreshold must be between 0 and 1.");
        if (Invalid(classification.GraphicStructureVetoThreshold)) failures.Add("MediaLibrary:Classification:GraphicStructureVetoThreshold must be between 0 and 1.");
        if (Invalid(classification.DiagramStructureVetoThreshold)) failures.Add("MediaLibrary:Classification:DiagramStructureVetoThreshold must be between 0 and 1.");
        if (Invalid(classification.ScreenshotThreshold) || Invalid(classification.DocumentThreshold) || Invalid(classification.DiagramThreshold) || Invalid(classification.PresentationThreshold) || Invalid(classification.GraphicThreshold)) failures.Add("All media classification thresholds must be between 0 and 1.");
        if (Invalid(classification.FacePresenceMinimumConfidence)) failures.Add("MediaLibrary:Classification:FacePresenceMinimumConfidence must be between 0 and 1.");
        if (classification.FacePresenceMinimumPixels is < 24 or > 2048) failures.Add("MediaLibrary:Classification:FacePresenceMinimumPixels must be between 24 and 2048.");
        if (classification.FacePresenceMinimumAreaRatio is < 0 or > 1) failures.Add("MediaLibrary:Classification:FacePresenceMinimumAreaRatio must be between 0 and 1.");
        if (classification.FacePresenceEvidenceBoost is < 0 or > 3 || double.IsNaN(classification.FacePresenceEvidenceBoost) || double.IsInfinity(classification.FacePresenceEvidenceBoost)) failures.Add("MediaLibrary:Classification:FacePresenceEvidenceBoost must be between 0 and 3.");
        if (classification.FacePresenceUnknownReduction is < 0 or > 1 || double.IsNaN(classification.FacePresenceUnknownReduction) || double.IsInfinity(classification.FacePresenceUnknownReduction)) failures.Add("MediaLibrary:Classification:FacePresenceUnknownReduction must be between 0 and 1.");
    }

    private static void ValidatePeopleOptions(MediaPeopleOptions people, ICollection<string> failures)
    {
        if (people.AutoConfirmEnabled)
        {
            failures.Add("MediaLibrary:People:AutoConfirmEnabled is not permitted. Every identity assignment requires a human decision.");
        }

        if (people.MaximumConcurrentAssets is < 1 or > 4)
            failures.Add("MediaLibrary:People:MaximumConcurrentAssets must be between 1 and 4 for CPU processing.");
        if (people.MaximumFacesPerAsset is < 1 or > 100)
            failures.Add("MediaLibrary:People:MaximumFacesPerAsset must be between 1 and 100.");
        if (people.MinimumFacePixels is < 32 or > 1024)
            failures.Add("MediaLibrary:People:MinimumFacePixels must be between 32 and 1024.");
        if (people.MinimumDetectionConfidence is < 0.1 or > 1)
            failures.Add("MediaLibrary:People:MinimumDetectionConfidence must be between 0.1 and 1.0.");
        if (people.NonMaximumSuppressionThreshold is <= 0 or >= 1)
            failures.Add("MediaLibrary:People:NonMaximumSuppressionThreshold must be greater than 0 and less than 1.");
        if (people.DetectorTopK is < 100 or > 100_000)
            failures.Add("MediaLibrary:People:DetectorTopK must be between 100 and 100000.");
        if (people.MinimumQualityScore is < 0 or > 1)
            failures.Add("MediaLibrary:People:MinimumQualityScore must be between 0 and 1.");
        if (people.MinimumClassificationConfidence is < 0 or > 1)
            failures.Add("MediaLibrary:People:MinimumClassificationConfidence must be between 0 and 1.");
        if (people.CandidateLimit is < 1 or > 20)
            failures.Add("MediaLibrary:People:CandidateLimit must be between 1 and 20.");
        if (people.CandidateSimilarityThreshold is < -1 or > 1)
            failures.Add("MediaLibrary:People:CandidateSimilarityThreshold must be between -1 and 1.");
        if (people.CandidateStrongSimilarityThreshold is < -1 or > 1)
            failures.Add("MediaLibrary:People:CandidateStrongSimilarityThreshold must be between -1 and 1.");
        if (people.CandidateStrongSimilarityThreshold < people.CandidateSimilarityThreshold)
            failures.Add("MediaLibrary:People:CandidateStrongSimilarityThreshold cannot be below CandidateSimilarityThreshold.");
        if (people.CandidateMinimumMargin is < 0 or > 1)
            failures.Add("MediaLibrary:People:CandidateMinimumMargin must be between 0 and 1.");
        if (people.CandidateRefreshBatchSize is < 1 or > 10_000)
            failures.Add("MediaLibrary:People:CandidateRefreshBatchSize must be between 1 and 10000.");
        if (people.CandidateRefreshIdleDelaySeconds is < 1 or > 3600)
            failures.Add("MediaLibrary:People:CandidateRefreshIdleDelaySeconds must be between 1 and 3600.");
        if (people.CandidateMinimumFaceQuality is < 0 or > 1)
            failures.Add("MediaLibrary:People:CandidateMinimumFaceQuality must be between 0 and 1.");
        if (people.CandidateBatchConfirmationLimit is < 1 or > 100)
            failures.Add("MediaLibrary:People:CandidateBatchConfirmationLimit must be between 1 and 100.");
        if (people.GroupingRefreshIntervalSeconds is < 5 or > 3600)
            failures.Add("MediaLibrary:People:GroupingRefreshIntervalSeconds must be between 5 and 3600.");
        if (people.GroupingMinimumFaces is < 2 or > 20)
            failures.Add("MediaLibrary:People:GroupingMinimumFaces must be between 2 and 20.");
        if (people.GroupingMaximumFaces is < 10 or > 25_000)
            failures.Add("MediaLibrary:People:GroupingMaximumFaces must be between 10 and 25000.");
        if (people.GroupingMaximumGroupSize is < 2 or > 500)
            failures.Add("MediaLibrary:People:GroupingMaximumGroupSize must be between 2 and 500.");
        if (people.GroupingSimilarityThreshold is < -1 or > 1)
            failures.Add("MediaLibrary:People:GroupingSimilarityThreshold must be between -1 and 1.");
        if (people.GroupingMinimumPairwiseSimilarity is < -1 or > 1)
            failures.Add("MediaLibrary:People:GroupingMinimumPairwiseSimilarity must be between -1 and 1.");
        if (people.GroupingMinimumPairwiseSimilarity > people.GroupingSimilarityThreshold)
            failures.Add("MediaLibrary:People:GroupingMinimumPairwiseSimilarity cannot exceed GroupingSimilarityThreshold.");
        if (people.ReferenceFacesPerPerson is < 1 or > 50)
            failures.Add("MediaLibrary:People:ReferenceFacesPerPerson must be between 1 and 50.");
        if (people.MaximumCandidateReferenceEmbeddings is < 100 or > 250_000)
            failures.Add("MediaLibrary:People:MaximumCandidateReferenceEmbeddings must be between 100 and 250000.");
        if (people.BatchSize is < 1 or > 16)
            failures.Add("MediaLibrary:People:BatchSize must be between 1 and 16.");
        if (people.IdleDelaySeconds is < 1 or > 3600)
            failures.Add("MediaLibrary:People:IdleDelaySeconds must be between 1 and 3600.");
        if (people.InferenceMaxDimension is < 320 or > 8192)
            failures.Add("MediaLibrary:People:InferenceMaxDimension must be between 320 and 8192.");
        if (string.IsNullOrWhiteSpace(people.ModelRoot))
            failures.Add("MediaLibrary:People:ModelRoot is required when face intelligence is enabled.");

        ValidateFaceModel(people.Detector, "Detector", failures, isDetector: true);
        ValidateFaceModel(people.Embedder, "Embedder", failures, isDetector: false);
    }

    private static void ValidateFaceModel(
        FaceModelOptions model,
        string label,
        ICollection<string> failures,
        bool isDetector)
    {
        if (string.IsNullOrWhiteSpace(model.Key)) failures.Add($"MediaLibrary:People:{label}:Key is required.");
        if (string.IsNullOrWhiteSpace(model.Version)) failures.Add($"MediaLibrary:People:{label}:Version is required.");
        if (string.IsNullOrWhiteSpace(model.Adapter)) failures.Add($"MediaLibrary:People:{label}:Adapter is required.");
        if (string.IsNullOrWhiteSpace(model.FileName)) failures.Add($"MediaLibrary:People:{label}:FileName is required.");
        var normalizedChecksum = model.Sha256?.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(normalizedChecksum)
            || normalizedChecksum.Length != 64
            || normalizedChecksum.Any(character => !Uri.IsHexDigit(character)))
        {
            failures.Add($"MediaLibrary:People:{label}:Sha256 must contain the approved 64-character hexadecimal SHA-256 checksum.");
        }
        if (string.IsNullOrWhiteSpace(model.License))
        {
            failures.Add($"MediaLibrary:People:{label}:License is required.");
        }

        var hasSourceUrl = !string.IsNullOrWhiteSpace(model.SourceUrl);
        var hasValidSourceUrl = hasSourceUrl
                                && Uri.TryCreate(model.SourceUrl, UriKind.Absolute, out var sourceUri)
                                && (string.Equals(sourceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        if (hasSourceUrl && !hasValidSourceUrl)
        {
            failures.Add($"MediaLibrary:People:{label}:SourceUrl must be an absolute HTTP or HTTPS URL.");
        }

        var hasOfflineProvenance = !string.IsNullOrWhiteSpace(model.Publisher)
                                   && !string.IsNullOrWhiteSpace(model.ApprovedArtifactId)
                                   && DateOnly.TryParseExact(
                                       model.AcquiredOn,
                                       "yyyy-MM-dd",
                                       CultureInfo.InvariantCulture,
                                       DateTimeStyles.None,
                                       out _);
        if (!hasValidSourceUrl && !hasOfflineProvenance)
        {
            failures.Add(
                $"MediaLibrary:People:{label} requires either SourceUrl or complete offline provenance " +
                "(Publisher, ApprovedArtifactId and AcquiredOn in yyyy-MM-dd format).");
        }
        if (model.InputWidth is < 32 or > 4096 || model.InputHeight is < 32 or > 4096)
            failures.Add($"MediaLibrary:People:{label} input dimensions must be between 32 and 4096 pixels.");
        if (model.InputScale <= 0 || !float.IsFinite(model.InputScale))
            failures.Add($"MediaLibrary:People:{label}:InputScale must be a positive finite value.");
        if (model.StdR <= 0 || model.StdG <= 0 || model.StdB <= 0)
            failures.Add($"MediaLibrary:People:{label} standard-deviation values must be positive.");
        if (!string.Equals(model.ChannelOrder, "RGB", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(model.ChannelOrder, "BGR", StringComparison.OrdinalIgnoreCase))
            failures.Add($"MediaLibrary:People:{label}:ChannelOrder must be RGB or BGR.");

        var supported = isDetector
            ? new[] { "YuNet", "DecodedDetections" }
            : new[] { "SFace", "GenericEmbedding" };
        if (!supported.Contains(model.Adapter, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"MediaLibrary:People:{label}:Adapter '{model.Adapter}' is unsupported. Supported values: {string.Join(", ", supported)}.");
        }

        if (!isDetector && model.EmbeddingDimension is < 16 or > 4096)
            failures.Add("MediaLibrary:People:Embedder:EmbeddingDimension must be between 16 and 4096.");
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
