using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class Training
{
    public Guid Id { get; set; }

    [Required]
    public Guid TrainingTypeId { get; set; }

    public TrainingType? TrainingType { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? TrainingMonth { get; set; }

    public int? TrainingYear { get; set; }

    [Range(0, int.MaxValue)]
    public int LegacyOfficerCount { get; set; }

    [Range(0, int.MaxValue)]
    public int LegacyJcoCount { get; set; }

    [Range(0, int.MaxValue)]
    public int LegacyOrCount { get; set; }

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

    public ICollection<TrainingProject> ProjectLinks { get; set; } = new HashSet<TrainingProject>();

    public ICollection<TrainingTrainee> Trainees { get; set; } = new HashSet<TrainingTrainee>();

    public TrainingCounters? Counters { get; set; }

    public ICollection<TrainingDeleteRequest> DeleteRequests { get; set; } = new HashSet<TrainingDeleteRequest>();
}
