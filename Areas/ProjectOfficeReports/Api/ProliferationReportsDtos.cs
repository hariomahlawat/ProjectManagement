using System.Text.Json.Serialization;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    // SECTION: Query DTO
    public sealed class ProliferationReportQueryDto
    {
        public ProliferationReportKind Report { get; set; }
        public ProliferationSource? Source { get; set; }

        public int? ProjectId { get; set; }
        public string? UnitName { get; set; }

        public int? ProjectCategoryId { get; set; }
        public int? TechnicalCategoryId { get; set; }

        // Bind from "YYYY-MM-DD" safely using DateTime then convert to DateOnly in service.
        public DateTime? FromDateUtc { get; set; }
        public DateTime? ToDateUtc { get; set; }

        // Approved default in service if empty. "All" supported.
        public string? ApprovalStatus { get; set; }

        // SECTION: Sorting
        public string? SortBy { get; set; }
        public string? SortDir { get; set; }

        // Paging
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    // SECTION: Column DTO
    public sealed class ProliferationReportColumnDto
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
    }

    // SECTION: Row DTO
    public sealed class ProliferationReportRowDto
    {
        // SECTION: Project fields
        public int? ProjectId { get; init; }
        public string? ProjectName { get; init; }
        public string? ProjectCode { get; init; }

        // SECTION: Source fields
        public ProliferationSource? Source { get; init; }
        public string? SourceLabel { get; init; }

        // SECTION: Unit fields
        public string? UnitName { get; init; }

        // SECTION: Date fields
        [JsonIgnore]
        public DateTime? ProliferationDateUtc { get; init; }

        [JsonPropertyName("proliferationDate")]
        public string? ProliferationDate => ProliferationDateUtc?.ToString("yyyy-MM-dd");

        public int? Year { get; init; }

        // SECTION: Quantities
        public int? Quantity { get; init; }
        public int? TotalQuantity { get; init; }

        // SECTION: Aggregates
        public int? UniqueUnits { get; init; }

        [JsonIgnore]
        public DateTime? FirstProliferationDateUtc { get; init; }

        [JsonIgnore]
        public DateTime? LastProliferationDateUtc { get; init; }

        [JsonPropertyName("firstDate")]
        public string? FirstProliferationDate => FirstProliferationDateUtc?.ToString("yyyy-MM-dd");

        [JsonPropertyName("lastDate")]
        public string? LastProliferationDate => LastProliferationDateUtc?.ToString("yyyy-MM-dd");

        // SECTION: Yearly reconciliation
        public int? YearlyApprovedTotal { get; init; }
        public int? GranularApprovedTotal { get; init; }
        public string? PreferenceMode { get; init; }
        public int? EffectiveTotal { get; init; }

        // SECTION: Status fields
        public string? Remarks { get; init; }
        public string? ApprovalStatus { get; init; }
    }

    // SECTION: Page DTO
    public sealed class ProliferationReportPageDto
    {
        public ProliferationReportKind Report { get; init; }
        public IReadOnlyList<ProliferationReportColumnDto> Columns { get; init; } = Array.Empty<ProliferationReportColumnDto>();
        public IReadOnlyList<ProliferationReportRowDto> Rows { get; init; } = Array.Empty<ProliferationReportRowDto>();

        public int Total { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
    }
}
