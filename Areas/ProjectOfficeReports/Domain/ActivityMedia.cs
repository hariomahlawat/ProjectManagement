using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class ActivityMedia
{
    public Guid Id { get; set; }

    [Required]
    public Guid ActivityId { get; set; }

    public MiscActivity? Activity { get; set; }

    [Required]
    [MaxLength(260)]
    public string StorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string MediaType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [MaxLength(256)]
    public string? Caption { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    [Required]
    [MaxLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;

    public DateTimeOffset UploadedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
