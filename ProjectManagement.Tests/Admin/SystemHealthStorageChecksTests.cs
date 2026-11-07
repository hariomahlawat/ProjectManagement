using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Problems that used to show only on their own pages now reach System health, and so Home's
// "To look at" (docs/ux/admin-review-fixes.md, PR 3). A cold review found 110 of 115 repository
// files missing and 402 of 420 media unavailable while System health said Storage was all OK.
public sealed class SystemHealthStorageChecksTests
{
    private const long Gb = 1024L * 1024L * 1024L;

    [Fact]
    public void MissingRepositoryFiles_AreAWarning_ThatSaysWhatItMeans()
    {
        var (status, summary, detail) = AdminSystemHealthService.RepositoryFilesResult(115, 110, capped: false);

        Assert.Equal(AdminHealthStatus.Warning, status);
        Assert.Equal("110 documents have lost their file, so they cannot be opened or downloaded.", summary);
        Assert.Equal("Checked 115 documents.", detail);
    }

    [Fact]
    public void RepositoryFiles_AllPresent_IsHealthy_AndACappedCheckSaysSo()
    {
        Assert.Equal(AdminHealthStatus.Healthy, AdminSystemHealthService.RepositoryFilesResult(115, 0, capped: false).Status);

        var capped = AdminSystemHealthService.RepositoryFilesResult(AdminSystemHealthService.RepositoryFileCheckLimit, 1, capped: true);
        Assert.Equal("1 document has lost its file, so it cannot be opened or downloaded.", capped.Summary);
        Assert.Equal("Checked the newest 5,000 documents.", capped.Detail);
    }

    [Fact]
    public void UnavailableMedia_IsAWarning()
    {
        var (status, summary, detail) = AdminSystemHealthService.MediaAvailabilityResult(available: 18, unavailable: 402);

        Assert.Equal(AdminHealthStatus.Warning, status);
        Assert.StartsWith("402 photos and videos cannot be found", summary);
        Assert.Equal("402 of 420 items unavailable.", detail);
        Assert.Equal(AdminHealthStatus.Healthy, AdminSystemHealthService.MediaAvailabilityResult(420, 0).Status);
    }

    [Theory]
    [InlineData(true, false, AdminHealthStatus.Warning)]
    [InlineData(true, true, AdminHealthStatus.Healthy)]
    [InlineData(false, false, AdminHealthStatus.Healthy)]
    public void FaceRecognition_WarnsOnlyWhenSwitchedOnButNotReady(bool enabled, bool ready, AdminHealthStatus expected)
    {
        var (status, _, detail) = AdminSystemHealthService.FaceDetectorResult(enabled, ready, "The approved detector file is missing.");

        Assert.Equal(expected, status);
        if (expected == AdminHealthStatus.Warning)
        {
            Assert.Equal("The approved detector file is missing.", detail); // the readiness message is passed through
        }
    }

    [Theory]
    [InlineData(40, AdminHealthStatus.Healthy)]
    [InlineData(27, AdminHealthStatus.Warning)]   // the cold review's 26.9 GB free against a 20 GB limit
    [InlineData(19, AdminHealthStatus.Critical)]
    public void ChatUploadSpace_WarnsBeforeUploadsStop(long freeGb, AdminHealthStatus expected)
    {
        var (status, _, detail) = AdminSystemHealthService.ChatUploadSpaceResult(freeGb * Gb, 20 * Gb);

        Assert.Equal(expected, status);
        Assert.Contains("chat uploads stop below 20", detail);
    }
}
