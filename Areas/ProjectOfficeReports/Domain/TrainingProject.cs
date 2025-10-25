using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class TrainingProject
{
    public Guid TrainingId { get; set; }

    public Training? Training { get; set; }

    public int ProjectId { get; set; }

    public Project? Project { get; set; }

    [Range(0, double.MaxValue)]
    public decimal AllocationShare { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
