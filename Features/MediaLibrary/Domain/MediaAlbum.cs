using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

/// <summary>
/// Organisation-wide curated media album. Albums are intentionally distinct from
/// source-derived collections such as Projects, Visits, Activities and Events.
/// Every authorised Photos user can view an active album; mutation rights are
/// enforced by the album application service.
/// </summary>
public sealed class MediaAlbum
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Description { get; set; }

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    [Required, MaxLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public bool IsArchived { get; set; }

    [MaxLength(450)]
    public string? ArchivedByUserId { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }

    public long? CoverMediaAssetId { get; set; }
    public MediaAsset? CoverMediaAsset { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public ICollection<MediaAlbumItem> Items { get; set; } = new List<MediaAlbumItem>();
}
