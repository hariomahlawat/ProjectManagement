using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public class ActionTaskAttachment
{
    [Key]
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int? UpdateId { get; set; }

    [Required, StringLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }

    [Required, StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, StringLength(1024)]
    public string StorageKey { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public bool IsDeleted { get; set; }
}
