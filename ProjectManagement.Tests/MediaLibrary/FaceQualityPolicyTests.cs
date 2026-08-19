using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceQualityPolicyTests
{
    [Theory]
    [InlineData(FaceQualityStatus.EmbeddingEligible, true)]
    [InlineData(FaceQualityStatus.Detected, true)]
    [InlineData(FaceQualityStatus.CropIncomplete, true)]
    [InlineData(FaceQualityStatus.LowResolution, false)]
    [InlineData(FaceQualityStatus.Blurred, false)]
    [InlineData(FaceQualityStatus.PoorExposure, false)]
    [InlineData(FaceQualityStatus.ExtremePose, false)]
    [InlineData(FaceQualityStatus.SeverelyCropped, false)]
    public void EmbeddingGeneration_IsBroaderThanPreferredReferenceSuitability(
        FaceQualityStatus status,
        bool expected)
    {
        Assert.Equal(expected, FaceQualityEvaluator.CanGenerateEmbedding(status));
    }
}
