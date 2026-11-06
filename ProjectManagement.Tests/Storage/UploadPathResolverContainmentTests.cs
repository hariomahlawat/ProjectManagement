using System;
using System.IO;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Storage;

/// <summary>
/// The containment check was a plain StartsWith(root), so a sibling folder sharing the root's
/// name as a prefix (uploads-x next to uploads) counted as inside the upload root.
/// </summary>
public sealed class UploadPathResolverContainmentTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "prism-upload-root-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public UploadPathResolverContainmentTests()
    {
        _root = Path.Combine(_parent, "uploads");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void AKeyInsideTheRoot_Resolves()
    {
        var resolver = new UploadPathResolver(new TestUploadRootProvider(_root));

        Assert.Equal(Path.Combine(_root, "docs", "a.pdf"), resolver.ToAbsolute("docs/a.pdf"));
        Assert.Equal("docs/a.pdf", resolver.ToRelative(Path.Combine(_root, "docs", "a.pdf")));
    }

    [Theory]
    [InlineData("../uploads-x/a.pdf")]
    [InlineData("../uploads2/a.pdf")]
    [InlineData("../a.pdf")]
    public void AKeyThatLeavesTheRoot_IsRefused_EvenIntoASiblingWithTheSamePrefix(string key)
    {
        var resolver = new UploadPathResolver(new TestUploadRootProvider(_root));

        Assert.Throws<InvalidOperationException>(() => resolver.ToAbsolute(key));
    }

    [Fact]
    public void ASiblingPath_IsNotMadeRelativeToTheRoot()
    {
        var resolver = new UploadPathResolver(new TestUploadRootProvider(_root));
        var sibling = Path.Combine(_parent, "uploads-x", "a.pdf");

        Assert.Equal(sibling, resolver.ToRelative(sibling));
    }

    [Fact]
    public void ARootWithATrailingSeparator_StillWorks()
    {
        var resolver = new UploadPathResolver(new TestUploadRootProvider(_root + Path.DirectorySeparatorChar));

        Assert.Equal(Path.Combine(_root, "a.pdf"), resolver.ToAbsolute("a.pdf"));
        Assert.Throws<InvalidOperationException>(() => resolver.ToAbsolute("../uploads-x/a.pdf"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_parent, recursive: true); } catch (IOException) { }
    }
}
