using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class ActivityType
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int Ordinal { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<MiscActivity> Activities { get; set; } = new HashSet<MiscActivity>();
}
