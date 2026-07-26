using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Arpp;

public sealed class ArppIssue
{
    public long Id { get; set; }

    public int FinancialYearStart { get; set; }

    public ArppIssueKind Kind { get; set; }

    public int IssueSequence { get; set; }

    [Required]
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ArppEntry> Entries { get; set; } = new List<ArppEntry>();

    public ArppAttachment? Attachment { get; set; }
}
