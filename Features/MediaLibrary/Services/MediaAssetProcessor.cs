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
        await _db.SaveChangesAsync(cancellationToken);

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
            }
            if (classify)
            {
                var eligibility = _classificationEligibility.Evaluate(asset, metadata);
                if (!eligibility.IsEligible)
                {
                    asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
                    asset.ProcessingFailureReason = null;
                }
                else
                {
                    var result = await _classifier.ClassifyAsync(content, metadata, cancellationToken);
                asset.Classification = result.Classification;
                asset.ClassificationConfidence = result.Confidence;
                    asset.AnalysisVersion = result.Version;
                    asset.ClassifierVersion = result.Version;
                    asset.AnalysisSignalsJson = JsonSerializer.Serialize(result.Signals);
                    asset.AnalysedAtUtc = DateTimeOffset.UtcNow;
                    asset.ClassifiedAtUtc = asset.AnalysedAtUtc;
                    asset.AnalysisStatus = MediaProcessingStatus.Ready;
                }
            }

            asset.Width = metadata.Width;
            asset.Height = metadata.Height;
            if (asset.MediaDateUtc == default) asset.MediaDateUtc = metadata.MediaDateUtc;
            asset.FileSizeBytes = metadata.FileSizeBytes > 0 ? metadata.FileSizeBytes : asset.FileSizeBytes;
            asset.FileModifiedAtUtc = metadata.FileModifiedAtUtc;
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (rebuild) asset.DerivativeStatus = MediaProcessingStatus.Failed;
            if (classify) asset.AnalysisStatus = MediaProcessingStatus.Failed;
            asset.ProcessingFailureReason = Trim(ex.GetBaseException().Message, 2048);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
