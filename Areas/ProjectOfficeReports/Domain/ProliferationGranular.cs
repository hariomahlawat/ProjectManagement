using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class ProliferationGranular
{
    public Guid Id { get; set; }

    [Required]
    public int ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required]
    public ProliferationSource Source { get; set; }

    [Range(1900, 9999)]
    public int Year { get; set; }

    [Required]
    public ProliferationGranularity Granularity { get; set; }

    [Range(1, 12)]
    public int Period { get; set; }

    [MaxLength(200)]
    public string? PeriodLabel { get; set; }

    public ProliferationMetrics Metrics { get; set; } = new();

    [MaxLength(2000)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
