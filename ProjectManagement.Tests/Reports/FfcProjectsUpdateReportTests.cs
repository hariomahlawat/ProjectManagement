using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Reports.FfcProjectsUpdate;
using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class FfcProjectsUpdateReportTests
{
    [Fact]
    public void Default_selection_excludes_only_country_years_where_every_project_is_installed()
    {
        var groups = new[]
        {
            Group(1, "Sri Lanka", "LKA", 2025, "Installed", "Installed"),
            Group(2, "Ethiopia", "ETH", 2025, "Installed", "Planned"),
            Group(3, "France", "FRA", 2026, "Planned")
        };

        var report = FfcProjectsUpdateReportFactory.Create(
            groups,
            FfcCountryYearSelectionMode.DefaultActive,
            selectedCountryYearsCsv: null,
            DateTimeOffset.UtcNow);

        Assert.Equal(3, report.AvailableCountryYearCount);
        Assert.Equal(2, report.SelectedCountryYearCount);
        Assert.Equal(1, report.DefaultExcludedCount);
        Assert.DoesNotContain(report.Groups, group => group.FfcRecordId == 1);
        Assert.Contains(report.Groups, group => group.FfcRecordId == 2);
        Assert.Contains(report.Groups, group => group.FfcRecordId == 3);
    }

    [Fact]
    public void Custom_selection_can_explicitly_include_an_all_installed_country_year()
    {
        var groups = new[]
        {
            Group(10, "Sri Lanka", "LKA", 2025, "Installed", "Installed"),
            Group(20, "Ethiopia", "ETH", 2025, "Planned")
        };

        var report = FfcProjectsUpdateReportFactory.Create(
            groups,
            FfcCountryYearSelectionMode.Custom,
            "10",
            DateTimeOffset.UtcNow);

        Assert.Single(report.Groups);
        Assert.Equal(10, report.Groups[0].FfcRecordId);
        Assert.True(report.CountryYears.Single(option => option.FfcRecordId == 10).IsAllInstalled);
        Assert.True(report.CountryYears.Single(option => option.FfcRecordId == 10).IsSelected);
    }

    [Fact]
    public void Custom_empty_selection_is_preserved_as_empty_and_does_not_revert_to_defaults()
    {
        var groups = new[]
        {
            Group(1, "France", "FRA", 2026, "Planned")
        };

        var report = FfcProjectsUpdateReportFactory.Create(
            groups,
            FfcCountryYearSelectionMode.Custom,
            string.Empty,
            DateTimeOffset.UtcNow);

        Assert.Empty(report.Groups);
        Assert.Equal(0, report.SelectedCountryYearCount);
        Assert.False(report.CanExport);
    }

    private static FfcDetailedGroupVm Group(
        long recordId,
        string country,
        string code,
        int year,
        params string[] statuses)
    {
        var rows = statuses
            .Select((status, index) => new FfcDetailedRowVm(
                FfcProjectId: recordId * 100 + index + 1,
                Serial: index + 1,
                ProjectName: $"Project {index + 1}",
                LinkedProjectId: index + 1,
                CostInCr: 0.7m,
                Quantity: index + 1,
                Status: status,
                ProgressText: "Current progress",
                ProgressTextRaw: "Current progress",
                ExternalRemarkId: null,
                ProgressSource: default,
                IsProgressEditable: false))
            .ToArray();

        return new FfcDetailedGroupVm(
            FfcRecordId: recordId,
            CountryName: country,
            CountryCode: code,
            Year: year,
            OverallRemarks: "Overall status",
            OverallRemarksDisplay: "Overall status",
            Rows: rows,
            HasIncomplete: rows.Any(row => !string.Equals(row.Status, "Installed", StringComparison.OrdinalIgnoreCase)));
    }
}
