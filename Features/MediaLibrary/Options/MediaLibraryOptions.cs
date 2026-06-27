namespace ProjectManagement.Features.MediaLibrary.Options;

/// <summary>
/// Feature switches for the media catalogue. Core PRISM Photos never depends on an
/// external source being configured or reachable.
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
}

public sealed class MediaPeopleOptions
{
    /// <summary>
    /// Deliberately disabled until approved open model weights and pgvector are deployed.
    /// </summary>
    public bool Enabled { get; set; }
    public bool WorkerEnabled { get; set; }
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
