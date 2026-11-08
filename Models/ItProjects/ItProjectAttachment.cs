using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// A file attached to an IT project (docs/ux/it-projects.md, "Attachments"): a PDF or Office
/// document kept under <c>{upload root}/it-projects/{projectId}/</c>. Soft-deleted rows keep
/// their file on disk, as the other soft deletes in PRISM do, so an administrator can restore
/// them later. Deliberately separate from the R&amp;D project document tables.
/// </summary>
public class ItProjectAttachment
{
    public const int FileNameMaxLength = 260;

    public Guid Id { get; set; } = Guid.NewGuid();

    public int ItProjectId { get; set; }

    public ItProject? ItProject { get; set; }

    /// <summary>The name the file had when it was uploaded, without any client path.</summary>
    [Required, MaxLength(FileNameMaxLength)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Relative key under the upload root: <c>it-projects/{projectId}/{guid}{ext}</c>.</summary>
    [Required, MaxLength(260)]
    public string StorageKey { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [Required, MaxLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DeletedByUserId { get; set; }
}
