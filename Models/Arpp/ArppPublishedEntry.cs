using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Arpp;

/// <summary>
/// Immutable row snapshot belonging to the latest published ARPP issue revision.
/// </summary>
public sealed class ArppPublishedEntry
{
    public long Id { get; set; }

    public long ArppIssueId { get; set; }

    /// <summary>
    /// Stable link to the management row from which this published row was produced.
    /// It is deliberately not a foreign key so the published snapshot survives corrections.
    /// </summary>
    public long SourceEntryId { get; set; }

    public int SortOrder { get; set; }

    [Required]
    [MaxLength(64)]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string ProjectReference { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    public ArppCategory Category { get; set; }

    public decimal IpaCost { get; set; }

    [Required]
    [MaxLength(200)]
    public string Cfa { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Fund { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string DfpdsSchedule { get; set; } = string.Empty;

    public ArppPublishedIssue PublishedIssue { get; set; } = null!;

    public Project? Project { get; set; }
}
