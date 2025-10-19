using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public sealed record ProliferationOverviewRequest(
    IReadOnlyCollection<int>? Years,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? ProjectCategoryId,
    int? TechnicalCategoryId,
    ProliferationSource? Source,
    string? Search,
    int Page,
    int PageSize);

public sealed record ProliferationOverviewResponse(
    ProliferationOverviewSummary Summary,
    PagedResult<ProliferationOverviewRow> Grid);

public sealed record ProliferationOverviewSummary(
    ProliferationOverviewKpiSet Totals,
    ProliferationOverviewKpiSet LastTwelveMonths);

public sealed record ProliferationOverviewKpiSet(
    int Projects,
    int TotalQuantity,
    int SddQuantity,
    int Abw515Quantity);

public sealed record ProliferationOverviewRow(
    int Year,
    string ProjectName,
    string? ProjectCode,
    ProliferationSource Source,
    string DataType,
    string? UnitName,
    string? SimulatorName,
    DateOnly? ProliferationDate,
    int Quantity,
    ApprovalStatus ApprovalStatus,
    YearPreferenceMode? Mode,
    Guid RecordId);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ProliferationYearlyRequestModel(
    int ProjectId,
    ProliferationSource Source,
    int Year,
    int TotalQuantity,
    string? Remarks);

public sealed record ProliferationGranularRequestModel(
    int ProjectId,
    string SimulatorName,
    string UnitName,
    DateOnly ProliferationDate,
    int Quantity,
    string? Remarks);

public sealed record ProliferationPreferenceRequestModel(
    int ProjectId,
    ProliferationSource Source,
    int Year,
    YearPreferenceMode Mode);

public sealed record ProliferationImportError(int RowNumber, string Message);

public sealed record ProliferationImportResult(IReadOnlyList<ProliferationImportError> Errors);
