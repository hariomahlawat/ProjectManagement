using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Infrastructure.Data;

public class IprAttachment
{
    public int Id { get; set; }

    [Required]
    public int IprRecordId { get; set; }

    public IprRecord Record { get; set; } = null!;

    [Required]
    [MaxLength(260)]
    public string StorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ContentType { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long FileSize { get; set; }

    [Required]
    [MaxLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;

    public ApplicationUser? UploadedByUser { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsArchived { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ArchivedByUserId { get; set; }

    public ApplicationUser? ArchivedByUser { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
