using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class FileSystemSourceHealthServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "prism-media-health",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TestAsync_ReportsReachableLocalFolderAndSamplesMedia()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "photo.jpg"), "test");
        await File.WriteAllTextAsync(Path.Combine(_root, "ignore.txt"), "test");
        var service = CreateService();

        var result = await service.TestAsync(
            _root,
            includeSubfolders: true,
            allowedExtensions: new[] { ".jpg" },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsReachable);
        Assert.Equal("Local folder", result.PathKind);
        Assert.Equal(1, result.SampleMediaCount);
    }

    [Fact]
    public async Task TestAsync_ReportsUnavailableFolderWithoutThrowing()
    {
        var service = CreateService();

        var result = await service.TestAsync(
            _root,
            includeSubfolders: false,
            allowedExtensions: new[] { ".jpg" },
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsReachable);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static FileSystemSourceHealthService CreateService()
        => new(
            new FileSystemPathResolver(),
            NullLogger<FileSystemSourceHealthService>.Instance);
}
