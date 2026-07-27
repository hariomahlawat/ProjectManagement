using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Arpp;

/// <summary>
/// Latest verified, organisation-visible snapshot of an ARPP issue.
/// The management workspace may be unlocked and edited without changing this published view.
/// </summary>
public sealed class ArppPublishedIssue
{
    public long ArppIssueId { get; set; }

    public int RevisionNumber { get; set; }

    public int FinancialYearStart { get; set; }

    public ArppIssueKind Kind { get; set; }

    public int IssueSequence { get; set; }

    [Required]
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    public DateTimeOffset PublishedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string PublishedByUserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string AttachmentStorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string AttachmentOriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string AttachmentContentType { get; set; } = "application/pdf";

    public long AttachmentSizeBytes { get; set; }

    [Required]
    [MaxLength(64)]
    public string AttachmentSha256 { get; set; } = string.Empty;

    public ArppIssue Issue { get; set; } = null!;

    public ICollection<ArppPublishedEntry> Entries { get; set; } = new List<ArppPublishedEntry>();
}
