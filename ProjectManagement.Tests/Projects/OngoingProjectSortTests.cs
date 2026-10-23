using System;
using System.Linq;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class OngoingProjectSortTests
{
    private static readonly OngoingProjectRowDto Alpha = Row("Alpha", stage: 3, age: 40, pdc: new DateOnly(2026, 11, 30), updatedAt: new DateTime(2026, 10, 20));
    private static readonly OngoingProjectRowDto Bravo = Row("Bravo", stage: 9, age: 300, pdc: new DateOnly(2026, 6, 22), updatedAt: new DateTime(2026, 5, 22));
    private static readonly OngoingProjectRowDto Charlie = Row("Charlie", stage: 5, age: null, pdc: null, updatedAt: null);

    private static readonly OngoingProjectRowDto[] Rows = { Alpha, Bravo, Charlie };

    [Fact]
    public void NoSort_KeepsTheServiceOrder()
    {
        Assert.Same(Rows, OngoingProjectSort.Apply(Rows, null, null));
        Assert.Same(Rows, OngoingProjectSort.Apply(Rows, "unknown", "asc"));
    }

    [Theory]
    [InlineData("name", null, "Alpha,Bravo,Charlie")]
    [InlineData("name", "desc", "Charlie,Bravo,Alpha")]
    [InlineData("stage", null, "Bravo,Charlie,Alpha")]          // later stages first
    [InlineData("age", null, "Bravo,Alpha,Charlie")]            // longest first; no age last
    [InlineData("age", "asc", "Alpha,Bravo,Charlie")]           // no age still last
    [InlineData("pdc", null, "Bravo,Alpha,Charlie")]            // earliest (overdue) first; no PDC last
    [InlineData("pdc", "desc", "Alpha,Bravo,Charlie")]          // no PDC still last
    [InlineData("update", null, "Charlie,Bravo,Alpha")]         // no update counts as the stalest
    [InlineData("update", "desc", "Alpha,Bravo,Charlie")]
    public void EachColumn_FirstPutsWhatNeedsAttentionOnTop(string key, string? direction, string expected)
    {
        var sorted = OngoingProjectSort.Apply(Rows, key, direction);

        Assert.Equal(expected, string.Join(",", sorted.Select(row => row.ProjectName)));
    }

    [Fact]
    public void Ties_FallBackToTheProjectName()
    {
        var rows = new[] { Row("Zulu", stage: 4, age: 10, pdc: null, updatedAt: null), Row("Echo", stage: 4, age: 10, pdc: null, updatedAt: null) };

        Assert.Equal(new[] { "Echo", "Zulu" }, OngoingProjectSort.Apply(rows, "stage", null).Select(row => row.ProjectName));
    }

    [Theory]
    [InlineData("age", null, null, "desc")]        // another column: the column's first direction
    [InlineData("age", "age", "desc", "asc")]      // already sorted that way: reverse it
    [InlineData("age", "age", "asc", "desc")]      // reversed: back to the first direction
    [InlineData("pdc", "age", "desc", "asc")]
    [InlineData("update", "update", "asc", "desc")]
    public void HeaderLinks_ToggleFromTheColumnsFirstDirection(string column, string? currentKey, string? currentDirection, string expected)
    {
        Assert.Equal(expected, OngoingProjectSort.NextDirection(column, currentKey, currentDirection));
    }

    [Theory]
    [InlineData(" PDC ", "pdc")]
    [InlineData("Age", "age")]
    [InlineData("", null)]
    [InlineData("cost", null)]
    public void Keys_AreNormalised(string? value, string? expected)
    {
        Assert.Equal(expected, OngoingProjectSort.NormalizeKey(value));
    }

    private static OngoingProjectRowDto Row(string name, int stage, int? age, DateOnly? pdc, DateTime? updatedAt) => new()
    {
        ProjectName = name,
        CurrentStageSortOrder = stage,
        DaysInCurrentStage = age,
        PresentStagePdc = pdc,
        RecentInternalRemarks = updatedAt is DateTime at
            ? new[] { new OngoingProjectRemarkDto { CreatedAtUtc = at, Text = "update" } }
            : Array.Empty<OngoingProjectRemarkDto>()
    };
}
