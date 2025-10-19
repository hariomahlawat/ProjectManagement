using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    // Query for /overview
    public sealed class ProliferationOverviewQuery
    {
        // Either Years[] or a Date range; if both are provided, prefer Date range
        public int[]? Years { get; set; }
        public DateTime? FromDateUtc { get; set; }
        public DateTime? ToDateUtc { get; set; }

        public int? ProjectCategoryId { get; set; }
        public int? TechnicalCategoryId { get; set; }
        public ProliferationSource? Source { get; set; }
        public string? Search { get; set; }

        // Paging
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    // KPIs payload
    public sealed class ProliferationKpisDto
    {
        public int TotalCompletedProjects { get; set; }
        public int TotalProliferationAllTime { get; set; }
        public int TotalProliferationSdd { get; set; }
        public int TotalProliferationAbw515 { get; set; }

        public int LastYearProjectsProliferated { get; set; }
        public int LastYearTotalProliferation { get; set; }
        public int LastYearSdd { get; set; }
        public int LastYearAbw515 { get; set; }
    }

    // One row in the results table
    public sealed class ProliferationOverviewRowDto
    {
        public int Year { get; set; }
        public string Project { get; set; } = default!;
        public string ProjectName => Project;
        public string? ProjectCode { get; set; }
        public ProliferationSource Source { get; set; }
        public string DataType { get; set; } = default!; // "Yearly" or "Granular"
        public string? UnitName { get; set; }
        public string? SimulatorName { get; set; }
        public DateTime? DateUtc { get; set; } // null for Yearly
        public string? ProliferationDate => DateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public int Quantity { get; set; }
        public string ApprovalStatus { get; set; } = default!;
        public string? Mode { get; set; } // only for consolidated rows in exports if needed
    }

    public sealed class ProliferationOverviewDto
    {
        public ProliferationKpisDto Kpis { get; set; } = new();
        public int TotalCount { get; set; }
        public IReadOnlyList<ProliferationOverviewRowDto> Rows { get; set; } = Array.Empty<ProliferationOverviewRowDto>();
    }

    // Create Yearly
    public sealed class ProliferationYearlyCreateDto
    {
        [Required] public int ProjectId { get; set; }
        [Required] public ProliferationSource Source { get; set; } // Sdd or Abw515
        [Range(2000, 3000)] public int Year { get; set; }
        [Range(0, int.MaxValue)] public int TotalQuantity { get; set; }
        [MaxLength(500)] public string? Remarks { get; set; }
    }

    // Create Granular (SDD only)
    public sealed class ProliferationGranularCreateDto
    {
        [Required] public int ProjectId { get; set; }
        [Required, MaxLength(200)] public string SimulatorName { get; set; } = default!;
        [Required, MaxLength(200)] public string UnitName { get; set; } = default!;
        [Required] public DateTime ProliferationDateUtc { get; set; }
        [Range(1, int.MaxValue)] public int Quantity { get; set; }
        [MaxLength(500)] public string? Remarks { get; set; }
    }

    // Year preference
    public sealed class ProliferationYearPreferenceDto
    {
        [Required] public int ProjectId { get; set; }
        [Required] public ProliferationSource Source { get; set; } // Sdd only configurable
        [Range(2000, 3000)] public int Year { get; set; }
        [Required] public YearPreferenceMode Mode { get; set; } // Auto, UseYearly, UseGranular
    }

    // Import results
    public sealed class ImportResultDto
    {
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public string? ErrorCsvBase64 { get; set; } // row-level errors as CSV (optional)
    }
}
