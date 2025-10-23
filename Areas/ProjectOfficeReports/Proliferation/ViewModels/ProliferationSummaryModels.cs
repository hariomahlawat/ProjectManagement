using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public sealed record ProliferationSummaryViewModel(
    IReadOnlyList<ProliferationSummaryProjectRow> ByProject,
    IReadOnlyList<ProliferationSummaryYearRow> ByYear,
    IReadOnlyList<ProliferationSummaryProjectYearRow> ByProjectYear)
{
    public static ProliferationSummaryViewModel Empty { get; } = new(
        Array.Empty<ProliferationSummaryProjectRow>(),
        Array.Empty<ProliferationSummaryYearRow>(),
        Array.Empty<ProliferationSummaryProjectYearRow>());
}

public sealed record ProliferationSummaryProjectRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSummarySourceTotals Totals);

public sealed record ProliferationSummaryYearRow(
    int Year,
    ProliferationSummarySourceTotals Totals);

public sealed record ProliferationSummaryProjectYearRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    int Year,
    ProliferationSummarySourceTotals Totals);

public sealed record ProliferationSummarySourceTotals(
    int Total,
    int Sdd,
    int Abw515);
