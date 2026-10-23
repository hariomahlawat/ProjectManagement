using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

/// <summary>The three views of FFC Projects, shown as tabs in one shared header.</summary>
public enum FfcView
{
    Overview,
    Footprint,
    DetailedTable
}

/// <summary>
/// The shared FFC Projects header (Partials/_FfcViewHeader.cshtml): one name, the view tabs and the
/// page's own actions. The tabs carry the year and country filters, so switching views keeps the
/// same scope.
/// </summary>
public sealed record FfcViewHeader(
    FfcView ActiveView,
    string Description,
    short? Year = null,
    long? CountryId = null,
    string? Query = null)
{
    public const string ModuleName = "FFC Projects";

    /// <summary>The page's own buttons, rendered on the right of the header.</summary>
    public Func<object?, object>? Actions { get; init; }

    public static string ViewLabel(FfcView view) => view switch
    {
        FfcView.Footprint => "Footprint",
        FfcView.DetailedTable => "Detailed table",
        _ => "Overview"
    };

    /// <summary>Browser title: "FFC Projects – Footprint".</summary>
    public static string PageTitle(FfcView view) => $"{ModuleName} – {ViewLabel(view)}";
}
