using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class ProliferationGranularRequest
{
    public Guid Id { get; set; }

    [Required]
    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

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
    public string SubmittedByUserId { get; set; } = string.Empty;

    public ApplicationUser SubmittedByUser { get; set; } = null!;

    public DateTimeOffset SubmittedAtUtc { get; set; }

    [Required]
    public ProliferationRequestDecisionState DecisionState { get; set; } = ProliferationRequestDecisionState.Pending;

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    public ApplicationUser? DecidedByUser { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    [MaxLength(2000)]
    public string? DecisionNotes { get; set; }

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
