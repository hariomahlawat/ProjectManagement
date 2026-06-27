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
    private readonly IFileSystemPathResolver _pathResolver;
    private readonly IMediaMetadataReader _metadataReader;
    private readonly IMediaClassifier _classifier;
    private readonly MediaLibraryOptions _options;

    public MediaAssetProcessor(
        MediaLibraryDbContext db,
        IMediaDerivativeService derivatives,
        IFileSystemPathResolver pathResolver,
        IMediaMetadataReader metadataReader,
        IMediaClassifier classifier,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _derivatives = derivatives ?? throw new ArgumentNullException(nameof(derivatives));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task ProcessAsync(
        long assetId,
        MediaProcessingJobType jobType,
        CancellationToken cancellationToken)
    {
        var asset = await _db.Assets
            .Include(item => item.Source)
            .SingleAsync(item => item.Id == assetId, cancellationToken);

        if (asset.Kind != MediaAssetKind.Photo)
        {
            asset.DerivativeStatus = MediaProcessingStatus.Unsupported;
            asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (asset.Source.SourceType != MediaLibrarySourceType.FileSystem
            || asset.Source.IsDeleted
            || string.IsNullOrWhiteSpace(asset.Source.RootPath)
            || string.IsNullOrWhiteSpace(asset.RelativePath))
        {
            asset.DerivativeStatus = MediaProcessingStatus.Ready;
            asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var rebuildDerivatives = jobType is MediaProcessingJobType.AnalyseAsset
            or MediaProcessingJobType.RebuildDerivatives;
        var classify = _options.Classification.Enabled
            && (jobType is MediaProcessingJobType.AnalyseAsset
                or MediaProcessingJobType.ReclassifyAsset);

        if (rebuildDerivatives)
        {
            asset.DerivativeStatus = MediaProcessingStatus.Processing;
        }

        if (classify)
        {
            asset.AnalysisStatus = MediaProcessingStatus.Processing;
        }
        else if (!_options.Classification.Enabled)
        {
            asset.AnalysisStatus = MediaProcessingStatus.NotRequested;
        }

        asset.ProcessingFailureReason = null;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var fullPath = _pathResolver.ResolveAssetPath(asset.Source.RootPath, asset.RelativePath);
            var metadata = await _metadataReader.ReadAsync(fullPath, cancellationToken);

            if (rebuildDerivatives)
            {
                await _derivatives.EnsureAsync(asset.Id, "thumb", cancellationToken);
                await _derivatives.EnsureAsync(asset.Id, "preview", cancellationToken);
                asset.DerivativeStatus = MediaProcessingStatus.Ready;
            }

            if (classify)
            {
                var classification = await _classifier.ClassifyAsync(fullPath, metadata, cancellationToken);
                asset.Classification = classification.Classification;
                asset.ClassificationConfidence = classification.Confidence;
                asset.AnalysisVersion = classification.Version;
                asset.AnalysisSignalsJson = JsonSerializer.Serialize(classification.Signals);
                asset.AnalysedAtUtc = DateTimeOffset.UtcNow;
                asset.AnalysisStatus = MediaProcessingStatus.Ready;
            }

            asset.Width = metadata.Width;
            asset.Height = metadata.Height;
            asset.MediaDateUtc = metadata.MediaDateUtc;
            asset.ProcessingFailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (rebuildDerivatives)
            {
                asset.DerivativeStatus = MediaProcessingStatus.Failed;
            }

            if (classify)
            {
                asset.AnalysisStatus = MediaProcessingStatus.Failed;
            }

            asset.ProcessingFailureReason = Trim(ex.GetBaseException().Message, 2048);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
