using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class TrainingCounters
{
    [Key]
    public Guid TrainingId { get; set; }

    public Training? Training { get; set; }

    [Range(0, int.MaxValue)]
    public int Officers { get; set; }

    [Range(0, int.MaxValue)]
    public int JuniorCommissionedOfficers { get; set; }

    [Range(0, int.MaxValue)]
    public int OtherRanks { get; set; }

    [Range(0, int.MaxValue)]
    public int Total { get; set; }

    public TrainingCounterSource Source { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
