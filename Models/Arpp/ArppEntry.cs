using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Arpp;

public sealed class ArppEntry
{
    public long Id { get; set; }

    public long ArppIssueId { get; set; }

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

    public int? CfaOptionId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Cfa { get; set; } = string.Empty;

    public int? FundOptionId { get; set; }

    [Required]
    [MaxLength(120)]
    public string Fund { get; set; } = string.Empty;

    public int? DfpdsScheduleId { get; set; }

    [Required]
    [MaxLength(120)]
    public string DfpdsSchedule { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ArppIssue Issue { get; set; } = null!;

    public Project? Project { get; set; }

    public ArppCfaOption? CfaOption { get; set; }

    public ArppFundOption? FundOption { get; set; }

    public ArppDfpdsSchedule? DfpdsScheduleOption { get; set; }
}
