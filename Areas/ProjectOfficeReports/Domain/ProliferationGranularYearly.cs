using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class ProliferationGranularYearly
{
    [Required]
    public int ProjectId { get; set; }

    [Required]
    public ProliferationSource Source { get; set; }

    [Range(1900, 9999)]
    public int Year { get; set; }

    public long? DirectBeneficiaries { get; set; }

    public long? IndirectBeneficiaries { get; set; }

    public decimal? InvestmentValue { get; set; }
}
