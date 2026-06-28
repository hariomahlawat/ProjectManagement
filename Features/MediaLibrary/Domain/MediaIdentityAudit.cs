using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaIdentityAudit
{
    public long Id { get; set; }
    public Guid? FaceId { get; set; }
    public Guid? PersonId { get; set; }
    public Guid? PreviousPersonId { get; set; }
    public Guid? NewPersonId { get; set; }

    [Required, MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(450)]
    public string PerformedByUserId { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Notes { get; set; }

    public string? MetadataJson { get; set; }
    public DateTimeOffset PerformedAtUtc { get; set; }
}
