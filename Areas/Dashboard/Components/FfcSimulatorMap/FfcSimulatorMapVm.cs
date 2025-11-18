using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.FfcSimulatorMap;

public sealed class FfcSimulatorMapVm
{
    public IReadOnlyList<FfcSimulatorCountryVm> Countries { get; init; } = Array.Empty<FfcSimulatorCountryVm>();

    public int TotalInstalled { get; init; }

    public int TotalDelivered { get; init; }

    public int TotalPlanned { get; init; }

    public int TotalCompleted => TotalInstalled + TotalDelivered;

    public int TotalUnits => TotalCompleted + TotalPlanned;

    public bool HasData => Countries.Count > 0;
}

public sealed class FfcSimulatorCountryVm
{
    public long CountryId { get; init; }

    public string Iso3 { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Installed { get; init; }

    public int Delivered { get; init; }

    public int Planned { get; init; }

    public int Total { get; init; }

    public int Completed => Installed + Delivered;
}
