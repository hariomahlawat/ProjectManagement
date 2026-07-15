using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public sealed record ProliferationProjectDetailViewModel(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    string? TechnicalCategoryName,
    ProliferationSummarySourceTotals Totals,
    IReadOnlyList<ProliferationProjectYearViewModel> Years)
{
    public static ProliferationProjectDetailViewModel Empty(int projectId) => new(
        projectId,
        "Project not found",
        null,
        null,
        new ProliferationSummarySourceTotals(0, 0, 0),
        Array.Empty<ProliferationProjectYearViewModel>());
}

public sealed record ProliferationProjectYearViewModel(
    int Year,
    ProliferationSummarySourceTotals Totals,
    IReadOnlyList<ProliferationProjectSourceYearViewModel> Sources);

public sealed record ProliferationProjectSourceYearViewModel(
    ProliferationSource Source,
    string SourceLabel,
    int AnnualQuantity,
    int DetailedQuantity,
    int DetailedEntryCount,
    int ReportedTotal,
    YearPreferenceMode EffectiveMode,
    string CalculationLabel,
    bool HasCountingException,
    string? AnnualRemarks,
    DateTime? LastUpdatedOnUtc,
    IReadOnlyList<ProliferationProjectDetailedEntryViewModel> DetailedEntries);

public sealed record ProliferationProjectDetailedEntryViewModel(
    Guid Id,
    DateOnly Date,
    string UnitName,
    int Quantity,
    string? Remarks);
