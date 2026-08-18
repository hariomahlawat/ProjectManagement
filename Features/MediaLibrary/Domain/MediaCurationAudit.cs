using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

/// <summary>
/// Lightweight durable audit for organisational album and editorial-caption changes.
/// It records curation decisions without duplicating the underlying media payload.
/// </summary>
public sealed class MediaCurationAudit
{
    public long Id { get; set; }

    [Required, MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    public Guid? MediaAlbumId { get; set; }
    public MediaAlbum? MediaAlbum { get; set; }

    public long? MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }

    [Required, MaxLength(450)]
    public string PerformedByUserId { get; set; } = string.Empty;

    public DateTimeOffset PerformedAtUtc { get; set; }

    public string? MetadataJson { get; set; }
}
