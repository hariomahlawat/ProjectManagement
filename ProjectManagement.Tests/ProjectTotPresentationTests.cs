using System;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotPresentationTests
{
    [Theory]
    [InlineData(ProjectTotStatus.NotRequired, "Not required")]
    [InlineData(ProjectTotStatus.NotStarted, "Not started")]
    [InlineData(ProjectTotStatus.InProgress, "In progress")]
    [InlineData(ProjectTotStatus.Completed, "Completed")]
    public void StatusLabel_IsTheSameEverywhere(ProjectTotStatus status, string expected)
        => Assert.Equal(expected, ProjectTotPresentation.StatusLabel(status));

    [Fact]
    public void BuildCard_PendingRequest_WinsOverCurrentStatus()
    {
        var card = ProjectTotPresentation.BuildCard(
            ProjectTotStatus.InProgress, new DateOnly(2024, 1, 1), PartialDatePrecision.Year,
            null, PartialDatePrecision.None, ProjectTotStatus.Completed, canManage: true);

        Assert.Equal(new ProjectTotCard("Approval pending", "Proposed: Completed", "info"), card);
    }

    [Fact]
    public void BuildCard_LegacyRowWithoutPrecision_StillShowsTheDate()
    {
        var card = ProjectTotPresentation.BuildCard(
            ProjectTotStatus.InProgress, new DateOnly(2023, 3, 14), PartialDatePrecision.None,
            null, PartialDatePrecision.None, null, canManage: false);

        Assert.Equal("Started 14 Mar 2023", card.Summary);
    }

    [Fact]
    public void BuildCard_KeepsStoredPrecision()
    {
        var card = ProjectTotPresentation.BuildCard(
            ProjectTotStatus.Completed, null, PartialDatePrecision.None,
            new DateOnly(2024, 12, 31), PartialDatePrecision.Year, null, canManage: true);

        Assert.Equal(new ProjectTotCard("ToT completed", "Completed 2024", "positive"), card);
    }

    [Theory]
    [InlineData(true, "Record ToT position")]
    [InlineData(false, "No ToT position recorded")]
    public void BuildCard_NoRecord_PromptsOnlyThoseWhoCanRecord(bool canManage, string expected)
    {
        var card = ProjectTotPresentation.BuildCard(
            null, null, PartialDatePrecision.None, null, PartialDatePrecision.None, null, canManage);

        Assert.Equal("Not recorded", card.Title);
        Assert.Equal(expected, card.Summary);
    }

    [Fact]
    public void DateInput_CompletedWithYearOnly_UsesEndOfYearAndYearPrecision()
    {
        var result = ProjectTotDateInput.Resolve(
            ProjectTotStatus.Completed,
            new PartialDateInput { Year = 2023, Month = 6 },
            new PartialDateInput { Year = 2024 });

        Assert.True(result.IsValid);
        Assert.Equal(new DateOnly(2023, 6, 1), result.StartedOn);
        Assert.Equal(PartialDatePrecision.Month, result.StartDatePrecision);
        Assert.Equal(new DateOnly(2024, 12, 31), result.CompletedOn);
        Assert.Equal(PartialDatePrecision.Year, result.CompletionDatePrecision);
    }

    [Fact]
    public void DateInput_InProgressWithCompletion_ReportsCompletionField()
    {
        var result = ProjectTotDateInput.Resolve(
            ProjectTotStatus.InProgress,
            new PartialDateInput { Year = 2024 },
            new PartialDateInput { Year = 2025 });

        var error = Assert.Single(result.Errors);
        Assert.Equal(ProjectTotDateInput.CompletionField, error.Field);
    }

    [Fact]
    public void DateInput_NotStartedWithDates_ReportsBothFields()
    {
        var result = ProjectTotDateInput.Resolve(
            ProjectTotStatus.NotStarted,
            new PartialDateInput { Year = 2024 },
            new PartialDateInput { Year = 2025 });

        Assert.Equal(2, result.Errors.Count);
        Assert.Null(result.StartedOn);
        Assert.Null(result.CompletedOn);
    }

    [Fact]
    public void FormatDay_UsesTheModuleDateStyle()
    {
        Assert.Equal("04 Mar 2026", ProjectTotPresentation.FormatDay(new DateOnly(2026, 3, 4)));
        Assert.Null(ProjectTotPresentation.FormatDay(null));
    }

    [Fact]
    public void FormatMoment_ShowsUtcInIst()
    {
        // 05:00 UTC is 10:30 IST.
        Assert.Equal("04 Mar 2026, 10:30", ProjectTotPresentation.FormatMoment(new DateTime(2026, 3, 4, 5, 0, 0, DateTimeKind.Utc)));
        Assert.Null(ProjectTotPresentation.FormatMoment(null));
    }
}
