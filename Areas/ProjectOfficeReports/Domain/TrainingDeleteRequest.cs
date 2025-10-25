using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class TrainingDeleteRequest
{
    public Guid Id { get; set; }

    [Required]
    public Guid TrainingId { get; set; }

    public Training? Training { get; set; }

    [Required]
    [MaxLength(450)]
    public string RequestedByUserId { get; set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public TrainingDeleteRequestStatus Status { get; set; } = TrainingDeleteRequestStatus.Pending;

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? DecisionNotes { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
