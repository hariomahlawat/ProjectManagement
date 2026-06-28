using ProjectManagement.Features.MediaLibrary.Domain;
namespace ProjectManagement.Features.MediaLibrary.Services;
public sealed record FaceModelReadiness(bool IsEnabled, bool IsReady, string Message, string? DetectorPath, string? EmbedderPath);
public sealed record DetectedFaceData(double Left,double Top,double Width,double Height,double Confidence,double QualityScore,FaceQualityStatus QualityStatus,float[]? Embedding,IReadOnlyList<double>? Landmarks,byte[]? ReviewThumbnail);
public interface IFaceModelReadinessService { Task<FaceModelReadiness> CheckAsync(CancellationToken cancellationToken); }
public interface IFaceAnalysisEngine { Task<IReadOnlyList<DetectedFaceData>> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken); }
public interface IFaceIntelligenceService { Task ProcessAssetAsync(long assetId, CancellationToken cancellationToken); }
public interface IFaceQueueService { Task<int> QueueEligibleAsync(int limit, CancellationToken cancellationToken); Task<bool> QueueAssetAsync(long assetId, CancellationToken cancellationToken); }
public interface IFaceReviewService
{
    Task<Guid> CreatePersonAndAssignAsync(Guid faceId,string displayName,string userId,CancellationToken cancellationToken);
    Task AssignAsync(Guid faceId,Guid personId,string userId,double? confidence,CancellationToken cancellationToken);
    Task RejectAsync(Guid faceId,Guid? personId,string userId,CancellationToken cancellationToken);
    Task SuppressAsync(Guid faceId,string userId,CancellationToken cancellationToken);
}
