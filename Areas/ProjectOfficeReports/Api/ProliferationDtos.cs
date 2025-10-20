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
        public int ProjectId { get; set; }
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
        public int EffectiveTotal { get; set; }
        public string ApprovalStatus { get; set; } = default!;
        public string? Mode { get; set; } // only for consolidated rows in exports if needed
    }

    public sealed class ProliferationOverviewDto
    {
        public ProliferationKpisDto Kpis { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public IReadOnlyList<ProliferationOverviewRowDto> Rows { get; set; } = Array.Empty<ProliferationOverviewRowDto>();
    }

    public sealed class ProliferationLookupOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
    }

    public sealed class ProliferationLookupsDto
    {
        public IReadOnlyList<ProliferationLookupOptionDto> ProjectCategories { get; set; }
            = Array.Empty<ProliferationLookupOptionDto>();

        public IReadOnlyList<ProliferationLookupOptionDto> TechnicalCategories { get; set; }
            = Array.Empty<ProliferationLookupOptionDto>();
    }

    public sealed class ProliferationProjectLookupDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Code { get; set; }
        public string Display => string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})";
    }

    // Create Yearly
    public class ProliferationYearlyCreateDto
    {
        [Required] public int ProjectId { get; set; }
        [Required] public ProliferationSource Source { get; set; } // Sdd or Abw515
        [Range(2000, 3000)] public int Year { get; set; }
        [Range(0, int.MaxValue)] public int TotalQuantity { get; set; }
        [MaxLength(500)] public string? Remarks { get; set; }
    }

    // Create Granular (SDD only)
    public class ProliferationGranularCreateDto
    {
        [Required] public int ProjectId { get; set; }
        [Required] public ProliferationSource Source { get; set; } = ProliferationSource.Sdd;
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

    public sealed class ProliferationManageListQueryDto
    {
        public int? ProjectId { get; set; }
        public ProliferationSource? Source { get; set; }
        public int? Year { get; set; }
        public string? Kind { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public sealed class ProliferationManageListItemDto
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = default!;
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = default!;
        public string? ProjectCode { get; set; }
        public ProliferationSource Source { get; set; }
        public string SourceLabel { get; set; } = default!;
        public int Year { get; set; }
        public int? Month { get; set; }
        public DateTime? ProliferationDateUtc { get; set; }
        public int Quantity { get; set; }
        public string ApprovalStatus { get; set; } = default!;
    }

    public sealed class ProliferationManageListResponseDto
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public IReadOnlyList<ProliferationManageListItemDto> Items { get; set; } = Array.Empty<ProliferationManageListItemDto>();
    }

    public sealed class ProliferationYearlyDetailDto
    {
        public Guid Id { get; set; }
        public int ProjectId { get; set; }
        public ProliferationSource Source { get; set; }
        public int Year { get; set; }
        public int TotalQuantity { get; set; }
        public string? Remarks { get; set; }
        public string RowVersion { get; set; } = default!;
    }

    public sealed class ProliferationGranularDetailDto
    {
        public Guid Id { get; set; }
        public int ProjectId { get; set; }
        public ProliferationSource Source { get; set; }
        public DateTime ProliferationDateUtc { get; set; }
        public string SimulatorName { get; set; } = default!;
        public string UnitName { get; set; } = default!;
        public int Quantity { get; set; }
        public string? Remarks { get; set; }
        public string RowVersion { get; set; } = default!;
    }

    public sealed class ProliferationYearlyUpdateDto : ProliferationYearlyCreateDto
    {
        [Required]
        public string RowVersion { get; set; } = default!;
    }

    public sealed class ProliferationGranularUpdateDto : ProliferationGranularCreateDto
    {
        [Required]
        public string RowVersion { get; set; } = default!;
    }
}
