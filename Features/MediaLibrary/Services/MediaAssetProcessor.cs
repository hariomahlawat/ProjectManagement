using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaAssetProcessor : IMediaAssetProcessor
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaDerivativeService _derivatives;
    private readonly IMediaContentProviderResolver _contentResolver;
    private readonly IMediaMetadataReader _metadataReader;
    private readonly IMediaClassifier _classifier;
    private readonly IMediaClassificationEligibilityService _classificationEligibility;
    private readonly MediaLibraryOptions _options;
    private readonly IFaceIntelligenceService _faceIntelligence;

    public MediaAssetProcessor(MediaLibraryDbContext db, IMediaDerivativeService derivatives,
        IMediaContentProviderResolver contentResolver, IMediaMetadataReader metadataReader,
        IMediaClassifier classifier, IMediaClassificationEligibilityService classificationEligibility,
        IOptions<MediaLibraryOptions> options, IFaceIntelligenceService faceIntelligence)
    {
        _db = db; _derivatives = derivatives; _contentResolver = contentResolver;
        _metadataReader = metadataReader; _classifier = classifier;
        _classificationEligibility = classificationEligibility; _options = options.Value; _faceIntelligence = faceIntelligence;
    }

    public async Task ProcessAsync(long assetId, MediaProcessingJobType jobType, CancellationToken cancellationToken)
    {
        if (jobType is MediaProcessingJobType.DetectFaces or MediaProcessingJobType.GenerateFaceEmbeddings or MediaProcessingJobType.AssignFaceCluster)
        {
            await _faceIntelligence.ProcessAssetAsync(assetId, cancellationToken);
            return;
        }
        var asset = await _db.Assets.Include(x => x.Source)
            .SingleAsync(x => x.Id == assetId, cancellationToken);
        if (!asset.IsAvailable || asset.IsDeleted)
        {
            asset.DerivativeStatus = MediaProcessingStatus.NotRequested;
            asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var processingCacheVersion = asset.CacheVersion;
        var processingClassificationToken = asset.ClassificationConcurrencyToken;

        if (asset.Kind != MediaAssetKind.Photo)
        {
            asset.DerivativeStatus = MediaProcessingStatus.Unsupported;
            asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var rebuild = jobType is MediaProcessingJobType.AnalyseAsset
            or MediaProcessingJobType.RebuildDerivatives
            or MediaProcessingJobType.BuildDerivatives;
        var classifyRequested = _options.Classification.Enabled
            && jobType is (MediaProcessingJobType.AnalyseAsset
                or MediaProcessingJobType.ReclassifyAsset
                or MediaProcessingJobType.ClassifyMedia
                or MediaProcessingJobType.RebuildIntelligence);
        var classify = classifyRequested && !asset.ClassificationIsManual;
        if (rebuild) asset.DerivativeStatus = MediaProcessingStatus.Processing;
        if (classify) asset.AnalysisStatus = MediaProcessingStatus.Processing;
        else if (!_options.Classification.Enabled) asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
        asset.ProcessingFailureReason = null;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // A catalogue refresh or human review changed the asset before processing began.
            // Ask the job owner to retry against the newer authoritative state.
            _db.ChangeTracker.Clear();
            throw new MediaProcessingSupersededException(
                $"Media asset {assetId} changed before processing began.",
                exception);
        }

        var derivativesCompleted = !rebuild;
        var classificationCompleted = !classify;
        try
        {
            var content = await _contentResolver.ResolveAsync(asset, cancellationToken)
                ?? throw new MediaContentUnavailableException(
                    $"The original media content could not be resolved for asset {asset.Id} ({asset.Origin}, {asset.SourceEntityId}).");
            var metadata = await _metadataReader.ReadAsync(content, cancellationToken);

            if (string.IsNullOrWhiteSpace(asset.ContentHash))
            {
                await using var hashStream = await content.OpenReadAsync(cancellationToken);
                var hash = await SHA256.HashDataAsync(hashStream, cancellationToken);
                asset.ContentHash = Convert.ToHexString(hash).ToLowerInvariant();
            }

            if (rebuild)
            {
                await _derivatives.EnsureAsync(asset.Id, "thumb", cancellationToken);
                await _derivatives.EnsureAsync(asset.Id, "preview", cancellationToken);
                asset.DerivativeStatus = MediaProcessingStatus.Ready;
                derivativesCompleted = true;
            }
            if (classify)
            {
                var eligibility = _classificationEligibility.Evaluate(asset, metadata);
                if (!eligibility.IsEligible)
                {
                    asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
                    asset.ProcessingFailureReason = null;
                    classificationCompleted = true;
                }
                else
                {
                    var result = await _classifier.ClassifyAsync(content, metadata, cancellationToken);
                    var currentReviewState = await _db.Assets
                        .AsNoTracking()
                        .Where(item => item.Id == asset.Id)
                        .Select(item => new
                        {
                            item.ClassificationConcurrencyToken,
                            item.ClassificationIsManual
                        })
                        .SingleAsync(cancellationToken);
                    if (currentReviewState.ClassificationIsManual
                        || currentReviewState.ClassificationConcurrencyToken != asset.ClassificationConcurrencyToken)
                    {
                        // A reviewer or catalogue invalidation won while inference was running.
                        // Retrying is safe: a manual result will be skipped and changed content will
                        // be processed using its new catalogue state.
                        _db.ChangeTracker.Clear();
                        throw new MediaProcessingSupersededException(
                            $"Classification for media asset {asset.Id} was superseded while inference was running.");
                    }
                    else
                    {
                        var now = DateTimeOffset.UtcNow;
                        var predictedScore = Convert.ToDecimal(Math.Clamp(result.PredictedScore, 0d, 1d));
                        asset.PredictedClassification = result.PredictedClassification;
                        asset.PredictedClassificationScore = predictedScore;
                        asset.Classification = result.EffectiveClassification;
                        asset.ClassificationConfidence = result.PredictedScore;
                        asset.ClassificationDecisionStatus = result.DecisionStatus;
                        asset.ClassificationDecisionReasonCode = result.DecisionReasonCode;
                        asset.AnalysisVersion = result.Version;
                        asset.ClassifierVersion = result.Version;
                        var signalsJson = JsonSerializer.Serialize(result.Signals);
                        asset.AnalysisSignalsJson = signalsJson;
                        asset.AutomaticClassificationSignalsJson = signalsJson;
                        asset.AutomaticClassificationScoresJson = JsonSerializer.Serialize(result.CategoryScores);
                        asset.AutomaticClassificationMetricsJson = JsonSerializer.Serialize(result.Telemetry);
                        asset.AnalysedAtUtc = now;
                        asset.ClassifiedAtUtc = now;
                        asset.ClassificationConcurrencyToken = Guid.NewGuid();
                        asset.AnalysisStatus = MediaProcessingStatus.Ready;
                        _db.ClassificationRuns.Add(new MediaClassificationRun
                        {
                            MediaAssetId = asset.Id,
                            ClassifierVersion = result.Version,
                            PredictedClassification = result.PredictedClassification,
                            PredictedScore = predictedScore,
                            EffectiveClassification = asset.Classification,
                            DecisionStatus = asset.ClassificationDecisionStatus,
                            DecisionReasonCode = asset.ClassificationDecisionReasonCode,
                            CategoryScoresJson = asset.AutomaticClassificationScoresJson,
                            SignalsJson = signalsJson,
                            MetricsJson = asset.AutomaticClassificationMetricsJson,
                            ProcessingDurationMilliseconds = result.ProcessingDurationMilliseconds,
                            CompletedAt = now,
                            Succeeded = true
                        });
                        classificationCompleted = true;
                    }
                }
            }

            await EnsureGenerationIsCurrentAsync(
                asset.Id,
                processingCacheVersion,
                processingClassificationToken,
                classify,
                cancellationToken);

            asset.Width = metadata.Width;
            asset.Height = metadata.Height;
            if (asset.MediaDateUtc == default) asset.MediaDateUtc = metadata.MediaDateUtc;
            asset.FileSizeBytes = metadata.FileSizeBytes > 0 ? metadata.FileSizeBytes : asset.FileSizeBytes;
            asset.FileModifiedAtUtc = metadata.FileModifiedAtUtc;
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // ClassificationConcurrencyToken protects human review and content invalidation.
            // Retry rather than recording a false classifier failure.
            _db.ChangeTracker.Clear();
            throw new MediaProcessingSupersededException(
                $"Media asset {assetId} changed before processing could be committed.",
                exception);
        }
        catch (MediaProcessingSupersededException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (await GenerationWasSupersededAsync(
                    assetId,
                    processingCacheVersion,
                    processingClassificationToken,
                    classify,
                    CancellationToken.None))
            {
                _db.ChangeTracker.Clear();
                throw new MediaProcessingSupersededException(
                    $"Media asset {assetId} was removed or replaced while processing was being cancelled.");
            }

            if (rebuild && !derivativesCompleted)
            {
                asset.DerivativeStatus = MediaProcessingStatus.Pending;
            }
            if (classify && !classificationCompleted)
            {
                asset.AnalysisStatus = MediaProcessingStatus.Pending;
            }
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            if (await GenerationWasSupersededAsync(
                    assetId,
                    processingCacheVersion,
                    processingClassificationToken,
                    classify,
                    CancellationToken.None))
            {
                _db.ChangeTracker.Clear();
                throw new MediaProcessingSupersededException(
                    $"Media asset {assetId} was removed or replaced while processing was running.",
                    ex);
            }

            if (rebuild && !derivativesCompleted)
            {
                asset.DerivativeStatus = MediaProcessingStatus.Failed;
            }
            if (classify && !classificationCompleted)
            {
                asset.AnalysisStatus = MediaProcessingStatus.Failed;
                asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.ProcessingFailed;
                asset.ClassificationDecisionReasonCode = "CLASSIFIER_PROCESSING_FAILED";
                _db.ClassificationRuns.Add(new MediaClassificationRun
                {
                    MediaAssetId = asset.Id,
                    ClassifierVersion = MediaClassifier.ClassifierVersion,
                    PredictedClassification = MediaClassification.Unknown,
                    PredictedScore = 0,
                    EffectiveClassification = MediaClassification.Unknown,
                    DecisionStatus = MediaClassificationDecisionStatus.ProcessingFailed,
                    DecisionReasonCode = "CLASSIFIER_PROCESSING_FAILED",
                    CategoryScoresJson = "{}",
                    SignalsJson = "[]",
                    MetricsJson = "{}",
                    ProcessingDurationMilliseconds = 0,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Succeeded = false,
                    FailureReason = Trim(ex.GetBaseException().Message, 2048)
                });
            }
            asset.ProcessingFailureReason = Trim(ex.GetBaseException().Message, 2048);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }


    private async Task EnsureGenerationIsCurrentAsync(
        long assetId,
        int expectedCacheVersion,
        Guid expectedClassificationToken,
        bool requireClassificationToken,
        CancellationToken cancellationToken)
    {
        if (await GenerationWasSupersededAsync(
                assetId,
                expectedCacheVersion,
                expectedClassificationToken,
                requireClassificationToken,
                cancellationToken))
        {
            _db.ChangeTracker.Clear();
            throw new MediaProcessingSupersededException(
                $"Media asset {assetId} was removed, replaced or reviewed while processing was running.");
        }
    }

    private async Task<bool> GenerationWasSupersededAsync(
        long assetId,
        int expectedCacheVersion,
        Guid expectedClassificationToken,
        bool requireClassificationToken,
        CancellationToken cancellationToken)
    {
        var current = await _db.Assets
            .AsNoTracking()
            .Where(item => item.Id == assetId)
            .Select(item => new
            {
                item.IsAvailable,
                item.IsDeleted,
                item.CacheVersion,
                item.ClassificationConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);

        return current is null
               || !current.IsAvailable
               || current.IsDeleted
               || current.CacheVersion != expectedCacheVersion
               || (requireClassificationToken
                   && current.ClassificationConcurrencyToken != expectedClassificationToken);
    }

    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
