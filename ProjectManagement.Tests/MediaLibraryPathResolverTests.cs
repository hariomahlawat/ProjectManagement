using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaLibraryPathResolverTests
{
    [Fact]
    public void ResolveAssetPath_AllowsFileWithinRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "prism-media", Guid.NewGuid().ToString("N"));
        var resolver = new NetworkSharePathResolver();

        var result = resolver.ResolveAssetPath(root, Path.Combine("Events", "photo.jpg"));

        Assert.True(result.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
        Assert.True(result.EndsWith(Path.Combine("Events", "photo.jpg"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveAssetPath_RejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "prism-media", Guid.NewGuid().ToString("N"));
        var resolver = new NetworkSharePathResolver();

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveAssetPath(root, Path.Combine("..", "secret.jpg")));
    }
}
