using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaMetadataReaderTests
{
    [Fact]
    public async Task ReadAsync_DoesNotReuseStreamOwnedBySkiaCodec()
    {
        var bytes = CreateJpeg(64, 48);
        var descriptor = new MediaContentDescriptor(
            "test.jpg",
            "image/jpeg",
            bytes.Length,
            DateTimeOffset.UtcNow,
            _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        var reader = CreateReader();

        var metadata = await reader.ReadAsync(descriptor, CancellationToken.None);

        Assert.Equal(64, metadata.Width);
        Assert.Equal(48, metadata.Height);
        Assert.Equal(bytes.Length, metadata.FileSizeBytes);
    }

    [Fact]
    public async Task ReadAsync_AcceptsNonSeekableReadableStream()
    {
        var bytes = CreateJpeg(32, 24);
        var descriptor = new MediaContentDescriptor(
            "test.jpg",
            "image/jpeg",
            null,
            DateTimeOffset.UtcNow,
            _ => Task.FromResult<Stream>(new NonSeekableReadStream(bytes)));
        var reader = CreateReader();

        var metadata = await reader.ReadAsync(descriptor, CancellationToken.None);

        Assert.Equal(32, metadata.Width);
        Assert.Equal(24, metadata.Height);
        Assert.Equal(bytes.Length, metadata.FileSizeBytes);
    }

    [Fact]
    public async Task ReadAsync_RejectsEmptyImageAsPermanentFailure()
    {
        var descriptor = new MediaContentDescriptor(
            "empty.jpg",
            "image/jpeg",
            0,
            DateTimeOffset.UtcNow,
            _ => Task.FromResult<Stream>(new MemoryStream()));
        var reader = CreateReader();

        await Assert.ThrowsAsync<MediaProcessingPermanentException>(
            () => reader.ReadAsync(descriptor, CancellationToken.None));
    }

    private static MediaMetadataReader CreateReader()
        => new(Options.Create(new MediaLibraryOptions
        {
            Processing = new MediaProcessingOptions
            {
                MaxImageFileSizeBytes = 10 * 1024 * 1024
            }
        }));

    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
