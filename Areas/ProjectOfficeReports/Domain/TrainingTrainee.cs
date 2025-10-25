using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class TrainingTrainee
{
    public int Id { get; set; }

    public Guid TrainingId { get; set; }
    public Training? Training { get; set; }

    [MaxLength(32)]
    public string? ArmyNumber { get; set; }

    [Required]
    [MaxLength(128)]
    public string Rank { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// 0 = Officer, 1 = JCO, 2 = OR.
    /// </summary>
    [Range(0, 2)]
    public byte Category { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
