using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// A remark on an IT project (docs/ux/it-projects.md, "Remarks"): what the ITO, MCO, HoD or
/// Comdt said, written as the role they held. Deliberately separate from the R&amp;D
/// <c>Remark</c> table, which is bound to <c>Projects</c>. No type, no mentions.
/// </summary>
public class ItProjectRemark
{
    public const int BodyMaxLength = 4000;

    public int Id { get; set; }

    public int ItProjectId { get; set; }

    public ItProject? ItProject { get; set; }

    [Required, MaxLength(450)]
    public string AuthorUserId { get; set; } = string.Empty;

    /// <summary>The Identity role the author wrote as: Comdt, HoD, MCO or ITO (<see cref="Configuration.RoleNames"/>).</summary>
    [Required, MaxLength(32)]
    public string AuthorRole { get; set; } = string.Empty;

    [Required, MaxLength(BodyMaxLength)]
    public string Body { get; set; } = string.Empty;

    /// <summary>The date the remark is about, not the date it was typed.</summary>
    public DateOnly EventDate { get; set; }

    /// <summary>Optional: the stage the remark is about (<see cref="ItProjectStages"/> code).</summary>
    [MaxLength(8)]
    public string? StageCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastEditedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DeletedByUserId { get; set; }

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ItProjectRemarkAudit> AuditEntries { get; set; } = new List<ItProjectRemarkAudit>();
}

public enum ItProjectRemarkAuditAction
{
    Created = 0,
    Edited = 1,
    Deleted = 2
}

/// <summary>An immutable snapshot of a remark after each create, edit or delete.</summary>
public class ItProjectRemarkAudit
{
    public int Id { get; set; }

    public int RemarkId { get; set; }

    public ItProjectRemark? Remark { get; set; }

    public ItProjectRemarkAuditAction Action { get; set; }

    [Required, MaxLength(450)]
    public string ActorUserId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? ActorRole { get; set; }

    public DateTime ActionAtUtc { get; set; }

    [Required, MaxLength(ItProjectRemark.BodyMaxLength)]
    public string SnapshotBody { get; set; } = string.Empty;

    public DateOnly SnapshotEventDate { get; set; }

    [MaxLength(8)]
    public string? SnapshotStageCode { get; set; }

    public bool SnapshotIsDeleted { get; set; }
}
