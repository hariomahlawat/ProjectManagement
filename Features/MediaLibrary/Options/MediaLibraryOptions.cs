namespace ProjectManagement.Features.MediaLibrary.Options;

/// <summary>
/// Feature switches for the media catalogue. Core PRISM Photos never depends on an
/// external source, an AI model, or a background worker being available.
/// </summary>
public sealed class MediaLibraryOptions
{
    public const string SectionName = "MediaLibrary";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Legacy configuration bind point. Database migrations are now governed by the
    /// mandatory application startup gate for both EF Core contexts.
    /// </summary>
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
    /// <summary>
    /// Optional second-based override for full PRISM reconciliation. Normal source changes
    /// use targeted outbox ingestion; complete scans are a repair safety net and should not
    /// run every few seconds. Set to 0 to use SynchronizeIntervalMinutes.
    /// </summary>
    public int SynchronizeIntervalSeconds { get; set; }

    /// <summary>
    /// Legacy configuration retained for backward compatibility. It is used only when
    /// SynchronizeIntervalSeconds is not positive.
    /// </summary>
    public int SynchronizeIntervalMinutes { get; set; } = 10;

    public TimeSpan GetSynchronizeInterval()
        => SynchronizeIntervalSeconds > 0
            ? TimeSpan.FromSeconds(SynchronizeIntervalSeconds)
            : TimeSpan.FromMinutes(Math.Max(1, SynchronizeIntervalMinutes));
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
    public double PresentationThreshold { get; set; } = 0.72;
    public double GraphicThreshold { get; set; } = 0.80;
    public double PhotographThreshold { get; set; } = 0.82;
    public double NaturalPhotoAutoAcceptThreshold { get; set; } = 0.88;
    /// <summary>Minimum separation between the winning and runner-up categories.</summary>
    public double MinimumScoreMargin { get; set; } = 0.10;
    /// <summary>Photographs require a wider margin because they can enter face processing.</summary>
    public double PhotographMinimumScoreMargin { get; set; } = 0.14;
    /// <summary>Final non-photograph probability at or above this score blocks automatic photograph acceptance.</summary>
    public double StrongConflictScore { get; set; } = 0.10;

    /// <summary>Minimum pre-face Photograph score before detector-only assistance may be considered.</summary>
    public double FaceProbeBasePhotographMinimumScore { get; set; } = 0.18;

    /// <summary>Minimum deterministic natural-photo structure score required for automatic Photograph admission.</summary>
    public double NaturalPhotoBaselineMinimumScore { get; set; } = 0.34;

    /// <summary>Pre-face non-photograph score at or above this value is a material conflict.</summary>
    public double BaseNonPhotoConflictScore { get; set; } = 0.35;

    /// <summary>Page/text structure at or above this value vetoes automatic Photograph admission.</summary>
    public double DocumentStructureVetoThreshold { get; set; } = 0.32;

    /// <summary>Designed-flat/illustration structure at or above this value vetoes automatic Photograph admission.</summary>
    public double GraphicStructureVetoThreshold { get; set; } = 0.55;

    /// <summary>Diagram-like line/page structure at or above this value vetoes automatic Photograph admission.</summary>
    public double DiagramStructureVetoThreshold { get; set; } = 0.50;

    public bool FacePresenceAssistanceEnabled { get; set; } = true;
    public double FacePresenceMinimumConfidence { get; set; } = 0.82;
    public int FacePresenceMinimumPixels { get; set; } = 48;
    public double FacePresenceMinimumAreaRatio { get; set; } = 0.01;

    /// <summary>Maximum raw-evidence contribution from a verified face. Deliberately bounded.</summary>
    public double FacePresenceEvidenceBoost { get; set; } = 1.65;

    /// <summary>Maximum raw Unknown-evidence reduction when verified face assistance is accepted.</summary>
    public double FacePresenceUnknownReduction { get; set; } = 0.30;
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

    /// <summary>
    /// Cosine thresholds for the approved OpenCV SFace embedding model. These are
    /// similarity gates, not probabilities. The lower threshold creates review-only
    /// suggestions; the strong threshold only changes presentation and never auto-confirms.
    /// </summary>
    /// <summary>Minimum aggregate similarity for a review-only known-person suggestion when two or more trusted references exist.</summary>
    public double CandidateSimilarityThreshold { get; set; } = 0.58;

    /// <summary>Stricter open-set gate used when a person has only one trusted reference.</summary>
    public double CandidateSingleReferenceSimilarityThreshold { get; set; } = 0.70;

    /// <summary>Absolute similarity floor for a strong candidate. Strong remains review-only.</summary>
    public double CandidateStrongSimilarityThreshold { get; set; } = 0.72;

    /// <summary>Minimum mean similarity across the strongest trusted references for a strong candidate.</summary>
    public double CandidateStrongMeanSimilarityThreshold { get; set; } = 0.68;

    /// <summary>Minimum best-vs-second-best person separation required for a strong candidate.</summary>
    public double CandidateMinimumMargin { get; set; } = 0.08;

    /// <summary>Strong evidence requires multiple independently trusted references.</summary>
    public int CandidateMinimumTrustedReferencesForStrong { get; set; } = 2;

    /// <summary>Low-quality confirmed appearances may never become matching references.</summary>
    public double CandidateMinimumTrustedReferenceQuality { get; set; } = 0.65;

    /// <summary>Enables durable background matching of new embeddings to confirmed people.</summary>
    public bool CandidateSearchEnabled { get; set; } = true;

    /// <summary>Maximum unassigned faces evaluated in one candidate-search cycle.</summary>
    public int CandidateRefreshBatchSize { get; set; } = 250;

    /// <summary>Delay between candidate-search discovery cycles when no work is available.</summary>
    public int CandidateRefreshIdleDelaySeconds { get; set; } = 15;

    /// <summary>Minimum face-quality score required before known-person matching is attempted.</summary>
    public double CandidateMinimumFaceQuality { get; set; } = 0.55;

    /// <summary>Maximum faces that may be human-confirmed together for one known person.</summary>
    public int CandidateBatchConfirmationLimit { get; set; } = 25;

    /// <summary>
    /// Groups unassigned, model-compatible embeddings into strict unnamed-person sets.
    /// Grouping never creates or confirms an identity; it only reduces human review effort.
    /// </summary>
    public bool GroupingEnabled { get; set; } = true;
    public int GroupingRefreshIntervalSeconds { get; set; } = 30;
    public int GroupingMinimumFaces { get; set; } = 2;
    public int GroupingMaximumFaces { get; set; } = 2_000;
    public int GroupingMaximumGroupSize { get; set; } = 50;

    /// <summary>
    /// Strict unnamed-face grouping gates. Every new member must satisfy both the
    /// centroid threshold and the complete-link pairwise floor. Faces from the same
    /// photograph are never grouped together.
    /// </summary>
    public double GroupingSimilarityThreshold { get; set; } = 0.42;
    public double GroupingMinimumPairwiseSimilarity { get; set; } = 0.38;

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
    public string Publisher { get; set; } = string.Empty;
    public string ApprovedArtifactId { get; set; } = string.Empty;
    public string AcquiredOn { get; set; } = string.Empty;
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
