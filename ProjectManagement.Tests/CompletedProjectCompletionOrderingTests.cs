using System;
using System.Linq;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class CompletedProjectCompletionOrderingTests
{
    [Fact]
    public void DescendingOrder_UsesRecordedCompletionComponentsAndKeepsUnknownLast()
    {
        var items = new[]
        {
            Item(1, "Unknown"),
            Item(2, "Year only 2026", completedYear: 2026),
            Item(3, "March 2026", completedYear: 2026, completedMonth: 3),
            Item(4, "05 March 2026", completedOn: new DateOnly(2026, 3, 5)),
            Item(5, "20 March 2026", completedOn: new DateOnly(2026, 3, 20)),
            Item(6, "December 2025", completedYear: 2025, completedMonth: 12)
        };

        var ordered = CompletedProjectCompletionOrdering
            .Apply(items, descending: true)
            .Select(item => item.Name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "20 March 2026",
                "05 March 2026",
                "March 2026",
                "Year only 2026",
                "December 2025",
                "Unknown"
            },
            ordered);
    }

    [Fact]
    public void CompletionDisplay_PreservesAvailablePrecisionWithoutInventingADate()
    {
        Assert.Equal(
            "20 Mar 2026",
            Item(1, "Exact", completedOn: new DateOnly(2026, 3, 20)).FormatCompletion());

        Assert.Equal(
            "Mar 2026",
            Item(2, "Month", completedYear: 2026, completedMonth: 3).FormatCompletion());

        Assert.Equal(
            "2026",
            Item(3, "Year", completedYear: 2026).FormatCompletion());

        Assert.Equal("—", Item(4, "Unknown").FormatCompletion());
    }

    private static CompletedProjectSummaryDto Item(
        int id,
        string name,
        DateOnly? completedOn = null,
        int? completedYear = null,
        short? completedMonth = null) =>
        new()
        {
            ProjectId = id,
            Name = name,
            CompletedOn = completedOn,
            CompletedYear = completedOn?.Year ?? completedYear,
            CompletedMonth = completedMonth
        };
}
