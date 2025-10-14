using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class SocialMediaEventPhoto
{
    public Guid Id { get; set; }

    [Required]
    public Guid SocialMediaEventId { get; set; }

    public SocialMediaEvent? SocialMediaEvent { get; set; }

    [Required]
    [MaxLength(260)]
    public string StorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string StoragePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ContentType { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    [MaxLength(512)]
    public string? Caption { get; set; }

    [MaxLength(64)]
    public string VersionStamp { get; set; } = string.Empty;

    public bool IsCover { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
