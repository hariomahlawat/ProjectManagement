using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class VisitPhoto
{
    public Guid Id { get; set; }

    [Required]
    public Guid VisitId { get; set; }

    public Visit? Visit { get; set; }

    [Required]
    [MaxLength(260)]
    public string StorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ContentType { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    [MaxLength(512)]
    public string? Caption { get; set; }

    [MaxLength(64)]
    public string VersionStamp { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
