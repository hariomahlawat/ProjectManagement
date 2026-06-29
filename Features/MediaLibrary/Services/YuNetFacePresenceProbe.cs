using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Executes only the approved detector. It never creates embeddings, thumbnails,
/// identities or database records and therefore remains independent of People activation.
/// </summary>
public sealed class YuNetFacePresenceProbe : IFacePresenceProbe
{
    private readonly IFacePresenceAnalysisEngine _engine;
    private readonly MediaClassificationOptions _options;

    public YuNetFacePresenceProbe(
        IFacePresenceAnalysisEngine engine,
        IOptions<MediaLibraryOptions> options)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _options = options?.Value.Classification
            ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<FacePresenceResult> AnalyseAsync(
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (!_options.FacePresenceAssistanceEnabled || imageBytes.Length == 0)
        {
            return new FacePresenceResult(true, false, 0, 0, 0, 0, 0, false);
        }

        try
        {
            return await _engine.AnalysePresenceAsync(imageBytes, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new FacePresenceResult(
                false,
                false,
                0,
                0,
                0,
                0,
                0,
                false,
                exception.GetBaseException().Message);
        }
    }
}
