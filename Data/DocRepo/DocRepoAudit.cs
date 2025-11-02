using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

public class DocRepoAudit
{
    public long Id { get; set; }

    public Guid? DocumentId { get; set; }

    [MaxLength(64)]
    public string EventType { get; set; } = null!;

    [Required, MaxLength(450)]
    public string ActorUserId { get; set; } = null!;

    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string DetailsJson { get; set; } = "{}";
}
