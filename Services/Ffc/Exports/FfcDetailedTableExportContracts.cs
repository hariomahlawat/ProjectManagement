using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Ffc.Exports;

public static class FfcDetailedTableExportDefaults
{
    public const string ReportTitle = "FFC Projects Update";
    public const string WordContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record FfcDetailedTableExportContext(
    string ScopeLabel,
    DateTimeOffset GeneratedAtUtc,
    string? HandlingMarking,
    IReadOnlyList<FfcDetailedGroupVm> Groups)
{
    public string Title => FfcDetailedTableExportDefaults.ReportTitle;

    public FfcDetailedTableExportSummary Summary => FfcDetailedTableExportSummary.Create(Groups);
}

public sealed record FfcDetailedTableExportSummary(
    int CountryCount,
    int RecordCount,
    int ProjectCount,
    int TotalQuantity,
    int InstalledQuantity,
    int DeliveredNotInstalledQuantity,
    int PlannedQuantity,
    decimal AvailableCostInLakh,
    int ProjectsWithCost)
{
    public string CostCoverageLabel => ProjectCount == 0
        ? "No projects"
        : $"{ProjectsWithCost} of {ProjectCount} projects";

    public static FfcDetailedTableExportSummary Create(IReadOnlyList<FfcDetailedGroupVm>? groups)
    {
        var safeGroups = groups ?? Array.Empty<FfcDetailedGroupVm>();
        var rows = safeGroups
            .Where(group => group.Rows is not null)
            .SelectMany(group => group.Rows)
            .ToArray();

        return new FfcDetailedTableExportSummary(
            CountryCount: safeGroups
                .Select(group => group.CountryName?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            RecordCount: safeGroups.Count(group => group.Rows is { Count: > 0 }),
            ProjectCount: rows.Length,
            TotalQuantity: rows.Sum(row => row.Quantity),
            InstalledQuantity: rows
                .Where(row => string.Equals(row.Status, "Installed", StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.Quantity),
            DeliveredNotInstalledQuantity: rows
                .Where(row => string.Equals(row.Status, "Delivered (not installed)", StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.Quantity),
            PlannedQuantity: rows
                .Where(row => string.Equals(row.Status, "Planned", StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.Quantity),
            AvailableCostInLakh: rows
                .Where(row => row.CostInCr.HasValue)
                .Sum(row => row.CostInCr!.Value * 100m),
            ProjectsWithCost: rows.Count(row => row.CostInCr.HasValue));
    }
}

public sealed record FfcDetailedTableExportFile(
    byte[] Content,
    string ContentType,
    string FileName);
