using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaContentProviderResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesFirstMatchingProvider()
    {
        var expected = new MediaContentDescriptor("photo.jpg", "image/jpeg", 12, null,
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[12])));
        var resolver = new MediaContentProviderResolver(new IMediaContentProvider[]
        {
            new FakeProvider(false, null),
            new FakeProvider(true, expected),
            new FakeProvider(true, null)
        });

        var result = await resolver.ResolveAsync(new MediaAsset { Origin = MediaAssetOrigin.ProjectPhoto }, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNoProviderMatches()
    {
        var resolver = new MediaContentProviderResolver(new IMediaContentProvider[] { new FakeProvider(false, null) });
        var result = await resolver.ResolveAsync(new MediaAsset(), CancellationToken.None);
        Assert.Null(result);
    }

    private sealed class FakeProvider(bool canHandle, MediaContentDescriptor? descriptor) : IMediaContentProvider
    {
        public bool CanHandle(MediaAsset asset) => canHandle;
        public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
            => Task.FromResult(descriptor);
    }
}
