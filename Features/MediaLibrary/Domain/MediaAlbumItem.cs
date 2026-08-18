using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

/// <summary>
/// Membership of a media asset in an organisation-wide album. The composite key
/// prevents duplicate membership while SortOrder provides deliberate curator order.
/// </summary>
public sealed class MediaAlbumItem
{
    public Guid MediaAlbumId { get; set; }
    public MediaAlbum MediaAlbum { get; set; } = null!;

    public long MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;

    public long SortOrder { get; set; }

    [Required, MaxLength(450)]
    public string AddedByUserId { get; set; } = string.Empty;

    public DateTimeOffset AddedAtUtc { get; set; }
}
