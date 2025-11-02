using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

public class DocumentDeleteRequest
{
    public long Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    [Required, MaxLength(450)]
    public string RequestedByUserId { get; set; } = null!;

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(512)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public bool IsApproved => ApprovedAtUtc.HasValue;
}
