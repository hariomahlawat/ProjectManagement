using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Scans never index links, but a catalogued file or folder could later be replaced by a
/// symbolic link pointing outside the source folder. Serving must not follow it.
/// </summary>
public sealed class MediaFileLinkServingTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"media-links-{Guid.NewGuid():N}");
    private readonly string _root;
    private readonly string _outside;

    public MediaFileLinkServingTests()
    {
        _root = Path.Combine(_workspace, "share");
        _outside = Path.Combine(_workspace, "outside");
        Directory.CreateDirectory(Path.Combine(_root, "album"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "secret.json"), "{\"password\":\"x\"}");
        File.WriteAllText(Path.Combine(_root, "album", "photo.jpg"), "jpeg");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task A_regular_file_is_served()
    {
        var descriptor = await ResolveAsync("album/photo.jpg");

        Assert.NotNull(descriptor);
    }

    [Fact]
    public async Task A_file_replaced_by_a_symbolic_link_is_not_served()
    {
        var linked = Path.Combine(_root, "album", "swapped.jpg");
        File.CreateSymbolicLink(linked, Path.Combine(_outside, "secret.json"));

        Assert.Null(await ResolveAsync("album/swapped.jpg"));
    }

    [Fact]
    public async Task A_folder_replaced_by_a_symbolic_link_is_not_served()
    {
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked-folder"), _outside);

        Assert.Null(await ResolveAsync("linked-folder/secret.json"));
    }

    private Task<MediaContentDescriptor?> ResolveAsync(string relativePath)
        => new FileSystemMediaContentProvider(new FileSystemPathResolver()).ResolveAsync(
            new MediaAsset
            {
                Origin = MediaAssetOrigin.ExternalFile,
                RelativePath = relativePath,
                OriginalFileName = Path.GetFileName(relativePath),
                ContentType = "image/jpeg",
                Source = new MediaLibrarySource { RootPath = _root }
            },
            CancellationToken.None);
}
