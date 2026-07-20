using System.ComponentModel.DataAnnotations;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api;

public sealed class ProliferationAnalysisRequestDto
{
    [Required]
    public ProliferationAnalysisScope Scope { get; set; } = ProliferationAnalysisScope.All;

    [Required]
    public ProliferationAnalysisPeriodMode PeriodMode { get; set; } = ProliferationAnalysisPeriodMode.AllTime;

    public int? TechnicalCategoryId { get; set; }

    public int[] ProjectIds { get; set; } = Array.Empty<int>();

    public int? Year { get; set; }

    public int? FromYear { get; set; }

    public int? ToYear { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public ProliferationSource? Source { get; set; }

    public bool IncludeUnitBreakdown { get; set; }
}

public sealed class ProliferationAnalysisResultDto
{
    public string ScopeLabel { get; init; } = string.Empty;

    public string PeriodLabel { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string CalculationBasis { get; init; } = string.Empty;

    public string CoverageMessage { get; init; } = string.Empty;

    public ProliferationAnalysisSummaryDto Summary { get; init; } = new();

    public IReadOnlyList<ProliferationAnalysisProjectRowDto> Projects { get; init; } =
        Array.Empty<ProliferationAnalysisProjectRowDto>();

    public IReadOnlyList<ProliferationAnalysisUnitRowDto> Units { get; init; } =
        Array.Empty<ProliferationAnalysisUnitRowDto>();
}

public sealed class ProliferationAnalysisSummaryDto
{
    public int TotalProliferation { get; init; }

    public int SddTotal { get; init; }

    public int Abw515Total { get; init; }

    public int ProjectCount { get; init; }

    public int TechnicalCategoryCount { get; init; }

    public int ReceivingUnitCount { get; init; }

    public int ApprovedAnnualQuantity { get; init; }

    public int ApprovedDetailedQuantity { get; init; }

    public int UnitBreakdownQuantity { get; init; }

    public bool HasUnitBreakdown { get; init; }
}

public sealed class ProliferationAnalysisProjectRowDto
{
    public int ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string? ProjectCode { get; init; }

    public string TechnicalCategory { get; init; } = "Not categorised";

    public int SddQuantity { get; init; }

    public int Abw515Quantity { get; init; }

    public int TotalQuantity { get; init; }
}

public sealed class ProliferationAnalysisUnitRowDto
{
    public string UnitName { get; init; } = string.Empty;

    public int ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string? ProjectCode { get; init; }

    public ProliferationSource Source { get; init; }

    public string SourceLabel { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public int EntryCount { get; init; }

    public DateOnly FirstDate { get; init; }

    public DateOnly LastDate { get; init; }
}
