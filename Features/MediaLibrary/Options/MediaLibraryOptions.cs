namespace ProjectManagement.Features.MediaLibrary.Options;

/// <summary>
/// Feature switches for the media catalogue. Core PRISM Photos never depends on an
/// external source, an AI model, or a background worker being available.
/// </summary>
public sealed class MediaLibraryOptions
{
    public const string SectionName = "MediaLibrary";

    public bool Enabled { get; set; } = true;
    public bool AutoMigrate { get; set; }
    public string CacheRoot { get; set; } = "App_Data/media-cache";

    public MediaCatalogueOptions Catalogue { get; set; } = new();
    public ExternalMediaSourcesOptions ExternalSources { get; set; } = new();
    public MediaProcessingOptions Processing { get; set; } = new();
    public MediaClassificationOptions Classification { get; set; } = new();
    public MediaPeopleOptions People { get; set; } = new();

    // Backward-compatible bind points for installations that briefly used the
    // first media-library prototype. New configuration must use the nested blocks.
    public bool? ScannerWorkerEnabled { get; set; }
    public bool? ProcessingWorkerEnabled { get; set; }
    public List<MediaSourceOptions> Sources { get; set; } = new();

    public bool IsCatalogueEnabled => Enabled && Catalogue.Enabled;

    private bool HasLegacyEnabledExternalSources =>
        ExternalSources.Sources.Count == 0
        && ScannerWorkerEnabled == true
        && Sources.Any(source => source.Enabled);

    public bool IsExternalSourceFeatureEnabled =>
        IsCatalogueEnabled && (ExternalSources.Enabled || HasLegacyEnabledExternalSources);

    public bool IsScannerWorkerEnabled =>
        IsExternalSourceFeatureEnabled
        && (ExternalSources.ScannerWorkerEnabled || ScannerWorkerEnabled == true);

    public bool IsProcessingWorkerEnabled =>
        IsCatalogueEnabled
        && (Processing.WorkerEnabled || ProcessingWorkerEnabled == true);

    public bool IsPeopleWorkerEnabled =>
        IsCatalogueEnabled
        && People.Enabled
        && People.WorkerEnabled;

    public bool IsAnyProcessingWorkerEnabled =>
        IsProcessingWorkerEnabled || IsPeopleWorkerEnabled;

    public IReadOnlyList<MediaSourceOptions> GetBootstrapSources()
        => ExternalSources.Sources.Count > 0
            ? ExternalSources.Sources
            : Sources.Where(source => source.Enabled).ToList();
}

public sealed class MediaCatalogueOptions
{
    public bool Enabled { get; set; } = true;
    public bool SynchronizePrismMedia { get; set; } = true;
    public int SynchronizeIntervalMinutes { get; set; } = 30;
}

public sealed class ExternalMediaSourcesOptions
{
    /// <summary>
    /// Master switch for local folders, NAS shares and folders shared by another server.
    /// </summary>
    public bool Enabled { get; set; }
    public bool ScannerWorkerEnabled { get; set; }
    public int DefaultScanIntervalMinutes { get; set; } = 30;
    public int ScanBatchSize { get; set; } = 250;
    public int IdleDelaySeconds { get; set; } = 20;
    public int ScanLeaseMinutes { get; set; } = 15;
    public List<MediaSourceOptions> Sources { get; set; } = new();
}

public sealed class MediaProcessingOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public int IdleDelaySeconds { get; set; } = 20;
    public int BatchSize { get; set; } = 1;
    public int MaxAttempts { get; set; } = 5;
    public long MaxImageFileSizeBytes { get; set; } = 209_715_200;
    public int ThumbnailMaxPixels { get; set; } = 480;
    public int PreviewMaxPixels { get; set; } = 1920;
    public int WebpQuality { get; set; } = 84;
}

public sealed class MediaClassificationOptions
{
    public bool Enabled { get; set; } = true;
    public bool ScreenshotDetectionEnabled { get; set; } = true;
    public bool DocumentDetectionEnabled { get; set; } = true;
    public bool DiagramDetectionEnabled { get; set; } = true;
    public double MinimumConfidence { get; set; } = 0.55;
    public int AnalysisMaxDimension { get; set; } = 512;
    public double ScreenshotThreshold { get; set; } = 0.62;
    public double DocumentThreshold { get; set; } = 0.66;
    public double DiagramThreshold { get; set; } = 0.64;
}

/// <summary>
/// Offline, opt-in face-intelligence controls. The default state is deliberately disabled.
/// No identity is automatically confirmed, regardless of similarity score.
/// </summary>
public sealed class MediaPeopleOptions
{
    public bool Enabled { get; set; }
    public bool WorkerEnabled { get; set; }
    public bool ProcessPhotographsOnly { get; set; } = true;
    public int MaximumConcurrentAssets { get; set; } = 1;
    public int MaximumFacesPerAsset { get; set; } = 25;
    public int MinimumFacePixels { get; set; } = 64;
    public double MinimumDetectionConfidence { get; set; } = 0.85;
    public double NonMaximumSuppressionThreshold { get; set; } = 0.30;
    public int DetectorTopK { get; set; } = 5000;
    public double MinimumQualityScore { get; set; } = 0.55;
    public double MinimumClassificationConfidence { get; set; } = 0.75;
    public int CandidateLimit { get; set; } = 5;
    public double CandidateSimilarityThreshold { get; set; } = 0.42;

    /// <summary>
    /// Retained only for configuration compatibility. The production service never
    /// auto-confirms an identity; enabling this value fails options validation.
    /// </summary>
    public bool AutoConfirmEnabled { get; set; }

    public int ReferenceFacesPerPerson { get; set; } = 8;
    public int MaximumCandidateReferenceEmbeddings { get; set; } = 20_000;
    public int BatchSize { get; set; } = 1;
    public int IdleDelaySeconds { get; set; } = 30;
    public int InferenceMaxDimension { get; set; } = 1600;
    public string ModelRoot { get; set; } = "App_Data/media-models";
    public FaceModelOptions Detector { get; set; } = new();
    public FaceModelOptions Embedder { get; set; } = new();
}

/// <summary>
/// Pinned model contract. Adapter names are intentionally explicit so an arbitrary ONNX
/// file cannot be interpreted using an incompatible tensor layout.
/// </summary>
public sealed class FaceModelOptions
{
    public string Key { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Adapter { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int InputWidth { get; set; } = 320;
    public int InputHeight { get; set; } = 320;
    public string InputName { get; set; } = "input";
    public string BoxesOutputName { get; set; } = "boxes";
    public string ScoresOutputName { get; set; } = "scores";
    public string LandmarksOutputName { get; set; } = "landmarks";
    public string EmbeddingOutputName { get; set; } = string.Empty;
    public int EmbeddingDimension { get; set; } = 128;
    public bool BoxesAreNormalized { get; set; } = true;
    public string ChannelOrder { get; set; } = "RGB";
    public float InputScale { get; set; } = 1f;
    public float MeanR { get; set; }
    public float MeanG { get; set; }
    public float MeanB { get; set; }
    public float StdR { get; set; } = 1f;
    public float StdG { get; set; } = 1f;
    public float StdB { get; set; } = 1f;
}

public sealed class MediaSourceOptions
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "FileSystem";
    public string RootPath { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool VisibleInLibrary { get; set; } = true;
    public bool ReadOnly { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;
    public int? ScanIntervalMinutes { get; set; }
    public List<string> AllowedExtensions { get; set; } = MediaSourceDefaults.AllowedExtensions.ToList();
}

public static class MediaSourceDefaults
{
    public static readonly string[] AllowedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp",
        ".mp4", ".webm", ".mov", ".m4v", ".ogg"
    };
}
