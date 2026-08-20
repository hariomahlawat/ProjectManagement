using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

/// <summary>
/// Explicit, human-governed one-to-one association between a confirmed Media Person and
/// an existing PRISM application account. No name or biometric similarity may create this
/// relationship automatically.
///
/// Profile presentation is deliberately separate from identity linkage. A linked user must
/// explicitly opt in before the representative Photos portrait is used as the PRISM avatar.
/// The account holder can also raise a governed concern if the media identity is not theirs.
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

    /// <summary>
    /// User-owned presentation preference. Linkage never enables this automatically.
    /// </summary>
    public bool UsePortraitAsAvatar { get; set; }

    public DateTimeOffset? ConcernRaisedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ConcernRaisedByUserId { get; set; }

    [MaxLength(1024)]
    public string? ConcernReason { get; set; }

    public DateTimeOffset? ConcernResolvedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ConcernResolvedByUserId { get; set; }

    [MaxLength(1024)]
    public string? ConcernResolution { get; set; }

    [MaxLength(450)]
    public string? UnlinkedByUserId { get; set; }

    public DateTimeOffset? UnlinkedAtUtc { get; set; }

    [MaxLength(1024)]
    public string? UnlinkReason { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public MediaPerson MediaPerson { get; set; } = null!;
}
