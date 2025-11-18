using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.ViewModels;

public sealed class FfcRecordCardViewModel
{
    public required FfcRecord Record { get; init; }
    public required FfcProjectQuantitySummary ProjectSummary { get; init; }
    public required bool CanManageRecords { get; init; }
    public int PageNumber { get; init; }
    public string CurrentSort { get; init; } = "year";
    public string CurrentSortDirection { get; init; } = "desc";
    public string? Query { get; init; }
    public IDictionary<string, string?> RouteValues { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
