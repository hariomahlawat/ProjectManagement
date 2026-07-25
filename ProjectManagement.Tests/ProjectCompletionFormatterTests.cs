using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectCompletionFormatterTests
{
    [Fact]
    public void Format_ExactDate_UsesDayMonthAndYear()
    {
        var display = ProjectCompletionFormatter.Format(
            new DateOnly(2026, 7, 25),
            2026,
            null);

        Assert.Equal("25 Jul 2026", display);
        Assert.Equal(
            ProjectCompletionPrecision.ExactDate,
            ProjectCompletionFormatter.InferPrecision(new DateOnly(2026, 7, 25), 2026, null));
    }

    [Fact]
    public void Format_MonthAndYear_DoesNotInventDay()
    {
        var display = ProjectCompletionFormatter.Format(null, 2026, 7);

        Assert.Equal("Jul 2026", display);
        Assert.Equal(
            ProjectCompletionPrecision.MonthAndYear,
            ProjectCompletionFormatter.InferPrecision(null, 2026, 7));
    }

    [Fact]
    public void Format_YearOnly_ReturnsYear()
    {
        Assert.Equal("2026", ProjectCompletionFormatter.Format(null, 2026, null));
    }

    [Fact]
    public void Format_Unknown_ReturnsConfiguredFallback()
    {
        Assert.Equal("Not recorded", ProjectCompletionFormatter.Format(null, null, null));
    }
}
