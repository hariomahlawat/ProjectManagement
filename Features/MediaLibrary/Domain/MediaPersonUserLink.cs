using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

/// <summary>
/// Explicit, human-governed one-to-one association between a confirmed Media Person and
/// an existing PRISM application account. No name or biometric similarity may create this
/// relationship automatically.
/// </summary>
public sealed class MediaPersonUserLink
{
    public Guid Id { get; set; }
    public Guid MediaPersonId { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(450)]
    public string LinkedByUserId { get; set; } = string.Empty;

    public DateTimeOffset LinkedAtUtc { get; set; }

    [MaxLength(450)]
    public string? UnlinkedByUserId { get; set; }

    public DateTimeOffset? UnlinkedAtUtc { get; set; }

    [MaxLength(1024)]
    public string? UnlinkReason { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public MediaPerson MediaPerson { get; set; } = null!;
}
