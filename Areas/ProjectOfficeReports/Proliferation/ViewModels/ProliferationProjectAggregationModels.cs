using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

// Section: Request models
public sealed record ProliferationProjectAggregationRequest(
    IReadOnlyCollection<int>? Years,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? ProjectCategoryId,
    int? TechnicalCategoryId,
    ProliferationSource? Source,
    string? Search);

// Section: Response models
public sealed record ProliferationProjectAggregationResult(
    IReadOnlyList<ProliferationProjectTotalsRow> AllProjectTotals,
    IReadOnlyList<ProliferationProjectYearTotalsRow> ProjectYearTotals,
    IReadOnlyList<ProliferationProjectUnitTotalsRow> ProjectUnitTotals,
    IReadOnlyList<ProliferationUnitProjectMapRow> UnitProjectMap)
{
    public static ProliferationProjectAggregationResult Empty { get; } = new(
        Array.Empty<ProliferationProjectTotalsRow>(),
        Array.Empty<ProliferationProjectYearTotalsRow>(),
        Array.Empty<ProliferationProjectUnitTotalsRow>(),
        Array.Empty<ProliferationUnitProjectMapRow>());
}

// Section: All-project totals
public sealed record ProliferationProjectTotalsRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSummarySourceTotals Totals);

// Section: Project-by-year totals
public sealed record ProliferationProjectYearTotalsRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    int Year,
    ProliferationSummarySourceTotals Totals);

// Section: Project-by-unit totals
public sealed record ProliferationProjectUnitTotalsRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    string UnitName,
    ProliferationSummarySourceTotals Totals);

// Section: Unit to project mapping
public sealed record ProliferationUnitProjectMapRow(
    string UnitName,
    IReadOnlyList<ProliferationUnitProjectItem> Projects);

public sealed record ProliferationUnitProjectItem(
    int ProjectId,
    string ProjectName,
    string? ProjectCode);
