using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

// The training register (Training/Index): order, date block, filter links and the old Records URL.
public sealed class TrainingRegisterTests
{
    [Fact]
    public void EffectiveDate_FallsBackToTheFirstOfTheMonthForMonthOnlyTrainings()
    {
        Assert.Equal(new DateOnly(2025, 3, 1), Item(month: 3, year: 2025).EffectiveDate);
        Assert.Equal(new DateOnly(2025, 6, 2), Item(start: new DateOnly(2025, 6, 2), month: 3, year: 2025).EffectiveDate);
        Assert.Null(Item().EffectiveDate);
    }

    [Fact]
    public void OrderForRegister_NewestFirst_SortsMonthOnlyTrainingsWithTheRest_AndUndatedLast()
    {
        var undated = Item(name: "undated");
        var march = Item(name: "march", month: 3, year: 2025);
        var june = Item(name: "june", start: new DateOnly(2025, 6, 2));
        var january = Item(name: "january", start: new DateOnly(2025, 1, 10));

        var ordered = TrainingTrackerReadService.OrderForRegister(new[] { undated, january, march, june }, TrainingRegisterSort.Newest);

        Assert.Equal(new[] { "june", "march", "january", "undated" }, ordered.Select(i => i.Notes));
    }

    [Fact]
    public void OrderForRegister_Oldest_Largest_AndType()
    {
        var a = Item(name: "a", type: "Simulator", start: new DateOnly(2025, 1, 1), total: 5);
        var b = Item(name: "b", type: "Drone", start: new DateOnly(2025, 2, 1), total: 9);
        var c = Item(name: "c", type: "Simulator", start: new DateOnly(2025, 3, 1), total: 1);
        var items = new[] { a, b, c };

        Assert.Equal(new[] { "a", "b", "c" }, TrainingTrackerReadService.OrderForRegister(items, TrainingRegisterSort.Oldest).Select(i => i.Notes));
        Assert.Equal(new[] { "b", "a", "c" }, TrainingTrackerReadService.OrderForRegister(items, TrainingRegisterSort.Largest).Select(i => i.Notes));
        // Type A–Z, newest first within a type.
        Assert.Equal(new[] { "b", "c", "a" }, TrainingTrackerReadService.OrderForRegister(items, TrainingRegisterSort.Type).Select(i => i.Notes));
    }

    [Theory]
    [InlineData("2026-10-12", "2026-10-16", 5)]
    [InlineData("2026-10-12", "2026-10-12", 1)]
    [InlineData("2026-10-12", null, 1)]
    [InlineData("2026-10-16", "2026-10-12", null)]
    [InlineData(null, "2026-10-12", null)]
    public void TrainingDuration_CountsDaysInclusively(string? start, string? end, int? expected)
    {
        DateOnly? Parse(string? value) => value is null ? null : DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, TrainingDuration.InclusiveDays(Parse(start), Parse(end)));
        Assert.Equal(expected, Item(start: Parse(start), end: Parse(end)).DurationDays);
    }

    [Fact]
    public void DateBlock_ShowsDayMonthAndSpan()
    {
        var range = IndexModel.TrainingRowViewModel.FromListItem(Item(start: new DateOnly(2026, 10, 12), end: new DateOnly(2026, 10, 16)));
        Assert.Equal("12", range.DateLead);
        Assert.Equal("Oct 2026", range.DateMonth);
        Assert.Equal("to 16 Oct · 5 days", range.DateNote);

        var crossYear = IndexModel.TrainingRowViewModel.FromListItem(Item(start: new DateOnly(2025, 12, 29), end: new DateOnly(2026, 1, 2)));
        Assert.Equal("to 02 Jan 2026 · 5 days", crossYear.DateNote);

        var oneDay = IndexModel.TrainingRowViewModel.FromListItem(Item(start: new DateOnly(2026, 5, 4)));
        Assert.Equal("1 day", oneDay.DateNote);

        var monthOnly = IndexModel.TrainingRowViewModel.FromListItem(Item(month: 3, year: 2025));
        Assert.Equal("Mar", monthOnly.DateLead);
        Assert.Equal("2025", monthOnly.DateMonth);
        Assert.Equal("Month only", monthOnly.DateNote);
    }

    [Fact]
    public void RouteWith_KeepsFilters_DropsDefaults_AndRemovesOneFilter()
    {
        var typeId = Guid.NewGuid();
        // RouteWith reads only the bound inputs, so the page's services are not needed.
        var page = (IndexModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(IndexModel));
        page.Filter = new IndexModel.FilterInput
        {
            TypeId = typeId,
            Category = TrainingCategory.OtherRank,
            From = new DateOnly(2025, 4, 1),
            Search = "  VR  "
        };
        page.Sort = "largest";
        page.PageSize = IndexModel.DefaultPageSize;

        var all = page.RouteWith(pageNumber: 3);
        Assert.Equal(typeId.ToString(), all["TypeId"]);
        Assert.Equal("OtherRank", all["Category"]);
        Assert.Equal("2025-04-01", all["From"]);
        Assert.Equal("VR", all["Search"]);
        Assert.Equal("largest", all["Sort"]);
        Assert.Equal("3", all["PageNumber"]);
        Assert.False(all.ContainsKey("PageSize"));

        var withoutType = page.RouteWith(removeKey: "TypeId");
        Assert.False(withoutType.ContainsKey("TypeId"));
        Assert.False(withoutType.ContainsKey("PageNumber"));
        Assert.True(page.HasFilters);
    }

    [Fact]
    public void Records_RedirectsToTheRegister_KeepingFiltersAndPage()
    {
        var page = new RecordsModel
        {
            Filter = new RecordsModel.FilterInput { Search = "VR", ProjectId = 42 },
            PageNumber = 2
        };

        var result = Assert.IsType<RedirectToPageResult>(page.OnGet());

        Assert.Equal("./Index", result.PageName);
        Assert.Equal("VR", result.RouteValues!["Search"]);
        Assert.Equal("42", result.RouteValues["ProjectId"]);
        Assert.Equal("2", result.RouteValues["PageNumber"]);
        Assert.False(result.RouteValues.ContainsKey("TypeId"));
    }

    private static TrainingListItem Item(
        string name = "item",
        string type = "Simulator",
        DateOnly? start = null,
        DateOnly? end = null,
        int? month = null,
        int? year = null,
        int total = 1)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            start,
            end,
            month,
            year,
            0,
            0,
            total,
            total,
            TrainingCounterSource.Roster,
            name,
            Array.Empty<TrainingProjectSnapshot>(),
            Array.Empty<byte>(),
            Array.Empty<string>(),
            string.Empty);
}
