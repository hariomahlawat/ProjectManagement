namespace ProjectManagement.Features.MediaLibrary.Options;

public sealed class MediaLibraryOptions
{
    public const string SectionName = "MediaLibrary";

    public bool Enabled { get; set; } = true;
    public bool ScannerWorkerEnabled { get; set; } = true;
    public bool ProcessingWorkerEnabled { get; set; } = true;
    public bool AutoMigrate { get; set; }

    public string CacheRoot { get; set; } = "App_Data/media-cache";
    public int ScanIntervalMinutes { get; set; } = 30;
    public int IdleDelaySeconds { get; set; } = 20;
    public int ScanBatchSize { get; set; } = 250;
    public int ProcessingBatchSize { get; set; } = 1;
    public int MaxAttempts { get; set; } = 5;
    public long MaxImageFileSizeBytes { get; set; } = 1_073_741_824;
    public int ThumbnailMaxPixels { get; set; } = 480;
    public int PreviewMaxPixels { get; set; } = 1920;
    public int WebpQuality { get; set; } = 84;

    public List<MediaSourceOptions> Sources { get; set; } = new();
}

public sealed class MediaSourceOptions
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "NetworkShare";
    public string RootPath { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ReadOnly { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;
    public int? ScanIntervalMinutes { get; set; }
    public List<string> AllowedExtensions { get; set; } = new()
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp",
        ".mp4", ".webm", ".mov", ".m4v"
    };
}
