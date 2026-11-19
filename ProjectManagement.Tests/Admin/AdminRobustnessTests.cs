using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Maintenance;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Robustness fixes from the second review (docs/ux/admin-review-2-fixes.md, PR 11).
public sealed class AdminRobustnessTests
{
    // The read blocks until the test releases it (an unreachable share does not answer), so it can
    // never finish before the time limit. With a fixed 3 s sleep it could: on a busy CI runner the
    // 200 ms timeout waits for a free thread-pool thread, the sleep ended first and its null result
    // came back instead of the warning (seen on #3459). What matters is that the check returns at
    // the limit without waiting for the read, so that is what is asserted, not a wall-clock time.
    [Fact]
    public async Task SlowFreeSpaceRead_IsStoppedAtTheTimeLimit_AndReportedAsAWarning()
    {
        using var release = new ManualResetEventSlim(false);
        var readFinished = 0;
        try
        {
            var check = await AdminSystemHealthService.RunWithinTimeLimitAsync(
                "storage-capacity",
                "Storage",
                "Disk space",
                () =>
                {
                    release.Wait(TimeSpan.FromSeconds(30)); // an unreachable share blocking the call
                    Interlocked.Exchange(ref readFinished, 1);
                    return null;
                },
                CancellationToken.None,
                TimeSpan.FromMilliseconds(200));

            Assert.Equal(0, Volatile.Read(ref readFinished));
            Assert.NotNull(check);
            Assert.Equal(AdminHealthStatus.Warning, check!.Status);
            Assert.Equal("The check took too long and was stopped.", check.Summary);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task QuickCheck_ReturnsItsOwnResult()
    {
        var expected = new AdminSystemHealthCheck("k", "Storage", "Disk space", AdminHealthStatus.Healthy, "Fine.", null, null, 0, DateTimeOffset.UtcNow);

        var check = await AdminSystemHealthService.RunWithinTimeLimitAsync(
            "k", "Storage", "Disk space", () => expected, CancellationToken.None, TimeSpan.FromSeconds(5));

        Assert.Same(expected, check);
    }

    [Fact]
    public void LegacyImportSweep_RemovesOnlyExpiredStagingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "prism-legacy-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
            var lifetime = TimeSpan.FromMinutes(30);

            var oldWorkbook = Touch(root, "old.xlsx", now.AddHours(-3));
            var oldNotes = Touch(root, "old.json", now.AddHours(-3));
            var recent = Touch(root, "recent.xlsx", now.AddMinutes(-50)); // inside lifetime + 1 h grace
            var other = Touch(root, "keep.txt", now.AddDays(-5));         // not a staging file

            var removed = LegacyImportPreflightService.SweepExpiredStaging(root, now, lifetime);

            Assert.Equal(2, removed);
            Assert.False(File.Exists(oldWorkbook));
            Assert.False(File.Exists(oldNotes));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyImportSweep_WithNoFolder_DoesNothing()
    {
        Assert.Equal(0, LegacyImportPreflightService.SweepExpiredStaging(
            Path.Combine(Path.GetTempPath(), "prism-missing-" + Guid.NewGuid().ToString("N")),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(30)));
    }

    private static string Touch(string root, string name, DateTimeOffset lastWrite)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
        return path;
    }
}
