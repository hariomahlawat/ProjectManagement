using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Arpp;

public sealed class ArppAttachment
{
    public long Id { get; set; }

    public long ArppIssueId { get; set; }

    [Required]
    [MaxLength(500)]
    public string StorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = "application/pdf";

    public long SizeBytes { get; set; }

    [Required]
    [MaxLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;

    public DateTimeOffset UploadedAtUtc { get; set; }

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ArppIssue Issue { get; set; } = null!;
}
