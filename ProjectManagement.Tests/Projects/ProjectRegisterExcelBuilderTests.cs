using ClosedXML.Excel;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectRegisterExcelBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);
    private static readonly DateTimeOffset ExportedAt = new(2026, 10, 22, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Active_WritesFactsAsNumbersAndDatesInScreenOrder()
    {
        var late = Row("Late project", facts: Facts(stageName: "Bid Upload", days: 82, pdc: Today.AddDays(-51), daysLate: 51, planned: 31, sinceUpdate: 4));
        var archived = Row("Archived project", facts: null);

        using var workbook = Open(new ProjectRegisterExcelBuilder().Build(Context(ProjectLifecycleFilter.Active, "Active", late, archived)));
        var sheet = workbook.Worksheet("Projects");

        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(
            new[] { "Ser", "Project", "Case file", "Category", "Current stage", "Stage state", "In stage since", "Days in stage", "Stage PDC", "Planned days", "Days late", "Days left", "Plan status", "Project Officer", "HoD", "Last update", "Days since update" },
            headers);
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);

        Assert.Equal(1, sheet.Cell(2, Column(headers, "Ser")).GetValue<int>());
        Assert.Equal("Late project", sheet.Cell(2, Column(headers, "Project")).GetString());
        Assert.Equal("Bid Upload", sheet.Cell(2, Column(headers, "Current stage")).GetString());
        Assert.Equal(82, sheet.Cell(2, Column(headers, "Days in stage")).GetValue<int>());
        Assert.Equal(51, sheet.Cell(2, Column(headers, "Days late")).GetValue<int>());
        Assert.True(sheet.Cell(2, Column(headers, "Days left")).IsEmpty());
        Assert.Equal("Late", sheet.Cell(2, Column(headers, "Plan status")).GetString());

        var pdc = sheet.Cell(2, Column(headers, "Stage PDC"));
        Assert.Equal(XLDataType.DateTime, pdc.DataType);
        Assert.Equal(new DateTime(2026, 9, 1), pdc.GetDateTime());
        Assert.Equal("dd-mmm-yyyy", pdc.Style.DateFormat.Format);

        Assert.Equal("Archived project", sheet.Cell(3, Column(headers, "Project")).GetString());
        Assert.Equal("Archived: timing not tracked", sheet.Cell(3, Column(headers, "Stage state")).GetString());
        Assert.True(sheet.Cell(3, Column(headers, "Days in stage")).IsEmpty());

        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.NotNull(sheet.AutoFilter.Range);
    }

    [Fact]
    public void Completed_Cancelled_And_All_UseTheirTabsColumns()
    {
        var completed = Row("Finished", closed: Closed(p => { p.LifecycleStatus = ProjectLifecycleStatus.Completed; p.CompletedYear = 2025; p.CompletedMonth = 11; p.IsLegacy = true; }));
        using (var workbook = Open(new ProjectRegisterExcelBuilder().Build(Context(ProjectLifecycleFilter.Completed, "Completed", completed))))
        {
            var sheet = workbook.Worksheet("Projects");
            var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Equal(new[] { "Ser", "Project", "Case file", "Category", "Build", "Completed", "ToT", "Record", "Project Officer", "HoD" }, headers);
            Assert.Equal("Nov 2025", sheet.Cell(2, Column(headers, "Completed")).GetString());
            Assert.Equal("Legacy entry", sheet.Cell(2, Column(headers, "Record")).GetString());
        }

        var cancelled = Row("Stopped", position: "Technical Evaluation · Ceased", cancelledOn: new DateOnly(2026, 2, 14),
            closed: Closed(p => { p.LifecycleStatus = ProjectLifecycleStatus.Cancelled; p.CancelReason = "Requirement withdrawn"; }));
        using (var workbook = Open(new ProjectRegisterExcelBuilder().Build(Context(ProjectLifecycleFilter.Cancelled, "Cancelled", cancelled))))
        {
            var sheet = workbook.Worksheet("Projects");
            var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Equal(new[] { "Ser", "Project", "Case file", "Category", "Ceased at", "Cancelled on", "Reason", "Project Officer", "HoD" }, headers);
            Assert.Equal(new DateTime(2026, 2, 14), sheet.Cell(2, Column(headers, "Cancelled on")).GetDateTime());
            Assert.Equal("Requirement withdrawn", sheet.Cell(2, Column(headers, "Reason")).GetString());
        }

        using (var workbook = Open(new ProjectRegisterExcelBuilder().Build(Context(ProjectLifecycleFilter.All, "All", completed))))
        {
            var headers = workbook.Worksheet("Projects").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Equal(new[] { "Ser", "Project", "Case file", "Category", "Status", "Current position", "Project Officer", "HoD" }, headers);
        }
    }

    [Fact]
    public void About_ListsTabFiltersOrderAndIstTime()
    {
        var context = Context(ProjectLifecycleFilter.Active, "Active", Row("One", facts: null)) with
        {
            Filters = new[] { "Search: drone", "Project Officer: K Srinath" }
        };

        using var workbook = Open(new ProjectRegisterExcelBuilder().Build(context));
        var about = workbook.Worksheet("About");
        var values = about.CellsUsed().Select(c => c.GetString()).ToList();

        Assert.Contains("PRISM Projects register", values);
        Assert.Contains("Blocked first, then most overdue", values);
        Assert.Contains("Search: drone", values);
        Assert.Contains("Project Officer: K Srinath", values);
        Assert.Contains("22-Oct-2026 12:00 IST", values);
    }

    [Fact]
    public void NoRows_StillWritesHeadersAndNoneForFilters()
    {
        using var workbook = Open(new ProjectRegisterExcelBuilder().Build(Context(ProjectLifecycleFilter.Active, "Active")));
        Assert.Equal(1, workbook.Worksheet("Projects").LastRowUsed()!.RowNumber());
        Assert.Contains(workbook.Worksheet("About").CellsUsed(), cell => cell.GetString() == "None");
    }

    private static int Column(IReadOnlyList<string> headers, string name) => headers.ToList().IndexOf(name) + 1;

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static ProjectRegisterExportContext Context(ProjectLifecycleFilter tab, string label, params ProjectRegisterExportRow[] rows)
        => new(tab, label, rows, Array.Empty<string>(), "Blocked first, then most overdue", ExportedAt);

    private static ProjectRegisterClosedRowVm Closed(Action<Project> setup)
    {
        var project = new Project { Name = "P", CreatedByUserId = "seed" };
        setup(project);
        return ProjectRegisterClosedRowVm.Create(project);
    }

    private static ProjectRegisterExportRow Row(
        string name,
        ProjectRegisterActiveFacts? facts = null,
        ProjectRegisterClosedRowVm? closed = null,
        string position = "Development · In progress",
        DateOnly? cancelledOn = null)
        => new(name, "50106/IR&D/HMV/SDD/24", "Other R&D Projects", "K Srinath", "Col S K Verma", position,
            closed ?? Closed(p => p.LifecycleStatus = ProjectLifecycleStatus.Active), cancelledOn, facts);

    private static ProjectRegisterActiveFacts Facts(string stageName, int days, DateOnly? pdc, int? daysLate, int? planned, int sinceUpdate)
        => new(
            1,
            Array.Empty<ProjectRegisterStageMark>(),
            0,
            "BID",
            stageName,
            ProjectRegisterStageState.InProgress,
            Today.AddDays(-days),
            days,
            pdc,
            planned,
            daysLate,
            daysLate is null && pdc is { } due ? due.DayNumber - Today.DayNumber : null,
            IsProlonged: false,
            IsApproachingProlonged: false,
            Today.AddDays(-sinceUpdate),
            sinceUpdate);
}
