using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaContentFingerprintTests
{
    [Fact]
    public void ComputeSha256_ReturnsFixedLengthDeterministicFingerprint()
    {
        var longStorageKey = $"activities/123/{new string('x', 300)}.jpg";

        var first = MediaContentFingerprint.ComputeSha256(
            new byte[] { 1, 2, 3, 4 },
            longStorageKey,
            15_000_000L,
            new DateTimeOffset(2026, 6, 30, 8, 0, 0, TimeSpan.Zero));
        var second = MediaContentFingerprint.ComputeSha256(
            new byte[] { 1, 2, 3, 4 },
            longStorageKey,
            15_000_000L,
            new DateTimeOffset(2026, 6, 30, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(64, first.Length);
        Assert.Equal(first, second);
        Assert.Matches("^[0-9A-F]{64}$", first);
    }

    [Fact]
    public void ComputeSha256_ChangesOnlyWhenContentIdentityInputChanges()
    {
        var original = MediaContentFingerprint.ComputeSha256(
            new byte[] { 1 }, "activities/7/photo.jpg", 100L,
            new DateTimeOffset(2026, 6, 30, 8, 0, 0, TimeSpan.Zero));
        var replaced = MediaContentFingerprint.ComputeSha256(
            new byte[] { 2 }, "activities/7/photo.jpg", 100L,
            new DateTimeOffset(2026, 6, 30, 8, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(original, replaced);
    }
}
