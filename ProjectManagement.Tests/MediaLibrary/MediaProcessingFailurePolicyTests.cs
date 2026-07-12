using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaProcessingFailurePolicyTests
{
    [Fact]
    public void MissingContent_IsPermanentAndSourceUnavailable()
    {
        var exception = new MediaContentUnavailableException("missing");

        Assert.True(MediaProcessingFailurePolicy.IsPermanent(exception));
        Assert.True(MediaProcessingFailurePolicy.IsSourceUnavailable(exception));
        Assert.True(MediaProcessingFailurePolicy.IsPermanentFailureCode(nameof(MediaContentUnavailableException)));
    }

    [Fact]
    public void IoFailure_IsRecoverable()
    {
        Assert.False(MediaProcessingFailurePolicy.IsPermanent(new IOException("busy")));
        Assert.False(MediaProcessingFailurePolicy.IsPermanentFailureCode(nameof(IOException)));
        Assert.Contains(nameof(IOException), MediaProcessingFailurePolicy.RecoverableFailureCodeNames);
    }

    [Fact]
    public void SourceUnavailableMarker_RoundTrips()
    {
        var value = MediaProcessingFailurePolicy.MarkSourceUnavailable("file missing");

        Assert.True(MediaProcessingFailurePolicy.HasSourceUnavailableMarker(value));
        Assert.False(MediaProcessingFailurePolicy.HasSourceUnavailableMarker("file missing"));
    }
}
