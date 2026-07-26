using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

public class ArppIssueInputModel
{
    [Range(2000, 9998)]
    public int FinancialYearStart { get; set; }

    [Required]
    public ArppIssueKind? Kind { get; set; }

    [Range(0, int.MaxValue)]
    public int IssueSequence { get; set; }

    [Required]
    [StringLength(300)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateOnly? IssueDate { get; set; }
}

public sealed class ArppWorkspaceInputModel : ArppIssueInputModel
{
    [Required]
    public string IssueRowVersion { get; set; } = string.Empty;

    public List<ArppEntryInputModel> Entries { get; set; } = [];
}

public sealed class ArppEntryInputModel
{
    public long? Id { get; set; }

    public string? RowVersion { get; set; }

    [Required]
    [StringLength(64)]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string ProjectReference { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    public string? LinkedProjectName { get; set; }

    public string? LinkedProjectMeta { get; set; }

    [Required]
    public ArppCategory? Category { get; set; }

    [Required]
    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal? IpaCost { get; set; }

    [Required]
    [StringLength(200)]
    public string Cfa { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Fund { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string DfpdsSchedule { get; set; } = string.Empty;
}

public sealed class ArppReconciliationInputModel
{
    public List<ArppReconciliationLinkInputModel> Links { get; set; } = [];
}

public sealed class ArppReconciliationLinkInputModel
{
    public long EntryId { get; set; }

    public string EntryRowVersion { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public string? ProjectMeta { get; set; }
}
