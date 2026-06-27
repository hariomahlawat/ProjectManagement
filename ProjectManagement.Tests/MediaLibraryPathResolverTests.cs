using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaLibraryPathResolverTests
{
    private readonly FileSystemPathResolver _resolver = new();

    [Fact]
    public void ResolveAssetPath_AllowsFileWithinLocalRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "prism-media", Guid.NewGuid().ToString("N"));

        var result = _resolver.ResolveAssetPath(root, Path.Combine("Events", "photo.jpg"));

        Assert.True(result.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
        Assert.True(result.EndsWith(Path.Combine("Events", "photo.jpg"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveAssetPath_RejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "prism-media", Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(() =>
            _resolver.ResolveAssetPath(root, Path.Combine("..", "secret.jpg")));
    }

    [Fact]
    public void ResolveAssetPath_RejectsAbsoluteAssetPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "prism-media", Guid.NewGuid().ToString("N"));
        var absolute = Path.Combine(Path.GetPathRoot(root)!, "outside", "photo.jpg");

        Assert.Throws<InvalidOperationException>(() =>
            _resolver.ResolveAssetPath(root, absolute));
    }


    [Fact]
    public void ResolveAssetPath_UsesCaseSensitiveBoundaryOnNonWindowsSystems()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Path.Combine(Path.GetTempPath(), "prism-media", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "Photos");
        var siblingWithDifferentCase = Path.Combine("..", "photos", "secret.jpg");

        Assert.Throws<InvalidOperationException>(() =>
            _resolver.ResolveAssetPath(root, siblingWithDifferentCase));
    }

    [Fact]
    public void DescribePathKind_ReportsLocalFolder()
    {
        var result = _resolver.DescribePathKind(Path.GetTempPath());

        Assert.Equal("Local folder", result);
    }

    [Fact]
    public void DescribePathKind_ReportsUncShareOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = _resolver.DescribePathKind(@"\\server\photos");

        Assert.Equal("UNC share", result);
    }
}
