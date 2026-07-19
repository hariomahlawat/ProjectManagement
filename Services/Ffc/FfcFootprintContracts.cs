using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Ffc;

public enum FfcFootprintMetric
{
    TotalUnits = 0,
    Installed = 1,
    Delivered = 2,
    Planned = 3
}

public enum FfcFootprintSort
{
    TotalUnits = 0,
    CountryName = 1,
    InstalledUnits = 2,
    PlannedUnits = 3,
    MostRecentActivity = 4
}

public sealed record FfcFootprintRequest(
    short? Year = null,
    long? CountryId = null,
    string? Search = null,
    FfcFootprintMetric Metric = FfcFootprintMetric.TotalUnits,
    FfcFootprintSort Sort = FfcFootprintSort.TotalUnits);

public sealed record FfcFootprintSummary(
    int CountryCount,
    int RecordCount,
    int ProjectCount,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;

    public int DeliveredUnits => InstalledUnits + DeliveredNotInstalledUnits;
}

public sealed record FfcFootprintCountryOption(
    long CountryId,
    string CountryName,
    string IsoCode);

public sealed record FfcFootprintProject(
    long FfcProjectId,
    int? LinkedProjectId,
    string DisplayName,
    string FfcName,
    int Quantity,
    FfcUnitPosition Position,
    string? StageSummary,
    string? CurrentProgress);

public sealed record FfcFootprintYear(
    long RecordId,
    short Year,
    int ProjectCount,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits,
    string? OverallPosition,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FfcFootprintProject> Projects)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;
}

public sealed record FfcFootprintCountry(
    long CountryId,
    string CountryName,
    string IsoCode,
    int RecordCount,
    int ProjectCount,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits,
    DateTimeOffset LastUpdated,
    IReadOnlyList<FfcFootprintYear> Years)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;

    public int ValueFor(FfcFootprintMetric metric) => metric switch
    {
        FfcFootprintMetric.Installed => InstalledUnits,
        FfcFootprintMetric.Delivered => DeliveredNotInstalledUnits,
        FfcFootprintMetric.Planned => PlannedUnits,
        _ => TotalUnits
    };
}

public sealed record FfcFootprintResult(
    FfcFootprintSummary Summary,
    IReadOnlyList<FfcFootprintCountry> Countries,
    IReadOnlyList<short> AvailableYears,
    IReadOnlyList<FfcFootprintCountryOption> CountryOptions)
{
    public static FfcFootprintResult Empty(
        IReadOnlyList<short>? availableYears = null,
        IReadOnlyList<FfcFootprintCountryOption>? countryOptions = null)
        => new(
            new FfcFootprintSummary(0, 0, 0, 0, 0, 0),
            Array.Empty<FfcFootprintCountry>(),
            availableYears ?? Array.Empty<short>(),
            countryOptions ?? Array.Empty<FfcFootprintCountryOption>());
}
