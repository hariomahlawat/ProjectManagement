using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Tests;

internal sealed class StubSocialMediaEventPhotoService : ISocialMediaEventPhotoService
{
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<SocialMediaEventPhoto>> _photos;
    private readonly IReadOnlyDictionary<(Guid EventId, Guid PhotoId), byte[]> _assets;

    public StubSocialMediaEventPhotoService()
        : this(new Dictionary<Guid, IReadOnlyList<SocialMediaEventPhoto>>(), new Dictionary<(Guid, Guid), byte[]>())
    {
    }

    public StubSocialMediaEventPhotoService(
        IReadOnlyDictionary<Guid, IReadOnlyList<SocialMediaEventPhoto>> photos,
        IReadOnlyDictionary<(Guid, Guid), byte[]> assets)
    {
        _photos = photos ?? throw new ArgumentNullException(nameof(photos));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public List<(Guid EventId, Guid PhotoId, string Size)> OpenRequests { get; } = new();

    public Task<IReadOnlyList<SocialMediaEventPhoto>> GetPhotosAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (_photos.TryGetValue(eventId, out var photos))
        {
            return Task.FromResult(photos);
        }

        return Task.FromResult<IReadOnlyList<SocialMediaEventPhoto>>(Array.Empty<SocialMediaEventPhoto>());
    }

    public Task<SocialMediaEventPhotoUploadResult> UploadAsync(
        Guid eventId,
        Stream content,
        string originalFileName,
        string? contentType,
        string? caption,
        string createdByUserId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<SocialMediaEventPhotoDeletionResult> RemoveAsync(
        Guid eventId,
        Guid photoId,
        byte[] rowVersion,
        string deletedByUserId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<SocialMediaEventPhotoSetCoverResult> SetCoverAsync(
        Guid eventId,
        Guid photoId,
        byte[] rowVersion,
        string modifiedByUserId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task RemoveAllAsync(Guid eventId, IReadOnlyCollection<SocialMediaEventPhoto> photos, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<SocialMediaEventPhotoAsset?> OpenAsync(Guid eventId, Guid photoId, string size, CancellationToken cancellationToken)
    {
        OpenRequests.Add((eventId, photoId, size));

        if (_assets.TryGetValue((eventId, photoId), out var content))
        {
            Stream stream = new MemoryStream(content, writable: false);
            return Task.FromResult<SocialMediaEventPhotoAsset?>(
                new SocialMediaEventPhotoAsset(stream, "image/jpeg", DateTimeOffset.UtcNow));
        }

        return Task.FromResult<SocialMediaEventPhotoAsset?>(null);
    }
}

internal sealed class CapturingSocialMediaPdfBuilder : ISocialMediaPdfReportBuilder
{
    public SocialMediaPdfReportContext? CapturedContext { get; private set; }

    public byte[] Build(SocialMediaPdfReportContext context)
    {
        CapturedContext = context ?? throw new ArgumentNullException(nameof(context));
        return new byte[] { 0x01, 0x02, 0x03 };
    }
}
