using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class TrainingRankCategoryMap
{
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Rank { get; set; } = string.Empty;

    public TrainingCategory Category { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
