using System.Globalization;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Services.Reports.FfcProjectsUpdate;

public enum FfcCountryYearSelectionMode
{
    DefaultActive = 0,
    Custom = 1
}

public sealed record FfcProjectsUpdatePresentationOptions(
    bool IncludeOverallStatus = false)
{
    public static FfcProjectsUpdatePresentationOptions Default { get; } = new();
}

public sealed record FfcCountryYearOption(
    long FfcRecordId,
    string CountryName,
    string CountryCode,
    int Year,
    bool IsAllInstalled,
    bool IsDefaultIncluded,
    bool IsSelected)
{
    public string Label => $"{CountryName} – {Year}";

    public string StatusLabel => IsAllInstalled ? "All installed" : "Active";
}

public sealed record FfcProjectsUpdateReport(
    DateTimeOffset GeneratedAtUtc,
    FfcCountryYearSelectionMode SelectionMode,
    IReadOnlyList<FfcCountryYearOption> CountryYears,
    IReadOnlyList<FfcDetailedGroupVm> Groups)
{
    public const string FormalTitle = "FFC PROJECTS UPDATE";

    public int AvailableCountryYearCount => CountryYears.Count;

    public int SelectedCountryYearCount => CountryYears.Count(option => option.IsSelected);

    public int DefaultExcludedCount => CountryYears.Count(option => option.IsAllInstalled);

    public int ProjectCount => Groups.Sum(group => group.Rows.Count);

    public int TotalQuantity => Groups.SelectMany(group => group.Rows).Sum(row => row.Quantity);

    public int InstalledProjectCount => Groups
        .SelectMany(group => group.Rows)
        .Count(row => string.Equals(row.Status, "Installed", StringComparison.OrdinalIgnoreCase));

    public bool CanExport => Groups.Count > 0 && ProjectCount > 0;

    public string SelectedCountryYearsCsv => string.Join(
        ",",
        CountryYears
            .Where(option => option.IsSelected)
            .Select(option => option.FfcRecordId.ToString(CultureInfo.InvariantCulture)));
}

public static class FfcProjectsUpdateReportFactory
{
    public static FfcProjectsUpdateReport Create(
        IReadOnlyList<FfcDetailedGroupVm>? sourceGroups,
        FfcCountryYearSelectionMode selectionMode,
        string? selectedCountryYearsCsv,
        DateTimeOffset generatedAtUtc)
    {
        var groups = (sourceGroups ?? Array.Empty<FfcDetailedGroupVm>())
            .Where(group => group.Rows is { Count: > 0 })
            .OrderByDescending(group => group.Year)
            .ThenBy(group => group.CountryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.FfcRecordId)
            .ToArray();

        var normalizedMode = Enum.IsDefined(selectionMode)
            ? selectionMode
            : FfcCountryYearSelectionMode.DefaultActive;

        var customSelection = normalizedMode == FfcCountryYearSelectionMode.Custom
            ? ParseSelectedIds(selectedCountryYearsCsv)
            : new HashSet<long>();

        var options = groups
            .Select(group =>
            {
                // The user-defined default rule is deliberately based on the
                // existing detailed-table Status contract: a country-year is
                // default-excluded only when every project is exactly Installed.
                var allInstalled = group.Rows.All(row =>
                    string.Equals(row.Status, "Installed", StringComparison.OrdinalIgnoreCase));

                var defaultIncluded = !allInstalled;
                var selected = normalizedMode == FfcCountryYearSelectionMode.DefaultActive
                    ? defaultIncluded
                    : customSelection.Contains(group.FfcRecordId);

                return new FfcCountryYearOption(
                    group.FfcRecordId,
                    group.CountryName,
                    group.CountryCode,
                    group.Year,
                    allInstalled,
                    defaultIncluded,
                    selected);
            })
            .ToArray();

        var selectedIds = options
            .Where(option => option.IsSelected)
            .Select(option => option.FfcRecordId)
            .ToHashSet();

        var selectedGroups = groups
            .Where(group => selectedIds.Contains(group.FfcRecordId))
            .ToArray();

        return new FfcProjectsUpdateReport(
            generatedAtUtc,
            normalizedMode,
            options,
            selectedGroups);
    }

    private static HashSet<long> ParseSelectedIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new HashSet<long>();
        }

        var result = new HashSet<long>();
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                result.Add(id);
            }
        }

        return result;
    }
}

public sealed record FfcProjectsUpdateFile(
    byte[] Content,
    string ContentType,
    string FileName);
