using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class TotTrackerSummaryTests
{
    [Fact]
    public void FromProjects_ComputesStatusAndRequestMetrics()
    {
        var rows = new List<ProjectTotTrackerRow>
        {
            CreateRow(
                projectId: 1,
                totStatus: ProjectTotStatus.Completed,
                requestState: ProjectTotRequestDecisionState.Approved,
                totMetCompletedOn: new DateOnly(2024, 1, 15),
                totFirstProductionModelManufactured: true),
            CreateRow(
                projectId: 2,
                totStatus: ProjectTotStatus.InProgress,
                requestState: ProjectTotRequestDecisionState.Pending),
            CreateRow(
                projectId: 3,
                totStatus: ProjectTotStatus.NotStarted,
                requestState: null),
            CreateRow(
                projectId: 4,
                totStatus: ProjectTotStatus.NotRequired,
                requestState: ProjectTotRequestDecisionState.Rejected)
        };

        var summary = IndexModel.TotTrackerSummary.FromProjects(rows);

        Assert.Equal(4, summary.TotalProjects);
        Assert.Equal(1, summary.TotNotRequired);
        Assert.Equal(3, summary.ProjectsRequiringTot);
        Assert.Equal(1, summary.TotNotStarted);
        Assert.Equal(1, summary.TotInProgress);
        Assert.Equal(1, summary.TotCompleted);
        Assert.Equal(1, summary.PendingApprovals);
        Assert.Equal(1, summary.ApprovedRequests);
        Assert.Equal(1, summary.RejectedRequests);
        Assert.Equal(1, summary.ProjectsWithMetCompleted);
        Assert.Equal(1, summary.ProjectsWithFirstProductionModel);
    }

    [Fact]
    public void FromProjects_WhenNoProjects_ReturnsEmptySummary()
    {
        var summary = IndexModel.TotTrackerSummary.FromProjects(Array.Empty<ProjectTotTrackerRow>());

        Assert.Equal(0, summary.TotalProjects);
        Assert.Equal(0, summary.ProjectsRequiringTot);
        Assert.Equal(0, summary.PendingApprovals);
    }

    [Fact]
    public void Narrative_CountsOnlyApplicableProjects_AndNamesNotRequiredApart()
    {
        var position = new ProjectTotPosition(
            NeedTot: 118, Completed: 65, InProgress: 0, NotStarted: 53, NotRequired: 21,
            WaitingForDecision: 0, LongWaiting: 23);

        Assert.Equal(
            "65 of 118 completed projects have completed Transfer of Technology (55%). 53 have not started. A further 21 projects do not require it.",
            ProjectTotOverview.Narrative(position));
    }

    [Fact]
    public void Narrative_ListsEveryOpenState()
    {
        var position = new ProjectTotPosition(
            NeedTot: 10, Completed: 1, InProgress: 2, NotStarted: 3, NotRequired: 0,
            WaitingForDecision: 0, LongWaiting: 0, NotKnown: 4);

        Assert.Equal(
            "1 of 10 completed projects has completed Transfer of Technology (10%). 2 are in progress, 3 have not started and for 4 it is not known.",
            ProjectTotOverview.Narrative(position));
    }

    [Fact]
    public void Narrative_WhenNothingNeedsToT()
    {
        Assert.Equal(
            "No completed project needs Transfer of Technology yet. A further 1 project does not require it.",
            ProjectTotOverview.Narrative(new ProjectTotPosition(0, 0, 0, 0, 1, 0, 0)));
    }

    [Theory]
    [InlineData(2022, 2025, "3 years after completion")]
    [InlineData(2024, 2025, "1 year after completion")]
    [InlineData(2025, 2025, "Same year as completion")]
    [InlineData(2025, 2024, "Before project completion")]
    public void RowNote_SaysHowLongAfterTheProjectToTWasCompleted(int projectYear, int totYear, string expected)
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            CreateRow(1, ProjectTotStatus.Completed, null, projectCompletedYear: projectYear, totCompletedOn: new DateOnly(totYear, 6, 1)),
            new DateOnly(2026, 11, 5));

        Assert.Equal(expected, ProjectTotOverview.RowNote(facts));
    }

    [Fact]
    public void RowNote_ForWaitingProjects()
    {
        var today = new DateOnly(2026, 11, 5);
        Assert.Equal("Waiting 28 years", ProjectTotOverview.RowNote(ProjectTotRegisterFactsBuilder.BuildFacts(CreateRow(1, ProjectTotStatus.NotStarted, null, projectCompletedYear: 1998), today)));
        Assert.Null(ProjectTotOverview.RowNote(ProjectTotRegisterFactsBuilder.BuildFacts(CreateRow(2, ProjectTotStatus.NotStarted, null, projectCompletedYear: 2024), today)));
        Assert.Equal("Waiting for a decision", ProjectTotOverview.RowNote(ProjectTotRegisterFactsBuilder.BuildFacts(CreateRow(3, ProjectTotStatus.NotStarted, ProjectTotRequestDecisionState.Pending, projectCompletedYear: 1998), today)));
    }

    [Fact]
    public void BuildYearlyCompletions_ShowsYearsWithoutCompletionsAsZero()
    {
        var rows = new List<ProjectTotTrackerRow>
        {
            CreateRow(1, ProjectTotStatus.Completed, null, totCompletedOn: new DateOnly(2009, 5, 1)),
            CreateRow(2, ProjectTotStatus.Completed, null, totCompletedOn: new DateOnly(2012, 1, 1)),
            CreateRow(3, ProjectTotStatus.Completed, null, totCompletedOn: new DateOnly(2012, 12, 31)),
            // Not counted: not completed, or completed without a date.
            CreateRow(4, ProjectTotStatus.InProgress, null, totCompletedOn: new DateOnly(2010, 1, 1)),
            CreateRow(5, ProjectTotStatus.Completed, null)
        };

        var points = ProjectTotOverview.BuildYearlyCompletions(rows);

        Assert.Equal(new[] { (2009, 1), (2010, 0), (2011, 0), (2012, 2) }, points);
        Assert.Empty(ProjectTotOverview.BuildYearlyCompletions(Array.Empty<ProjectTotTrackerRow>()));
    }

    [Fact]
    public void OldSummaryAddress_OpensTheOneToTPage()
    {
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(new SummaryModel().OnGet());

        Assert.Equal("/Tot/Index", result.PageName);
        Assert.Equal("ProjectOfficeReports", result.RouteValues?["area"]);
    }

    private static ProjectTotTrackerRow CreateRow(
        int projectId,
        ProjectTotStatus? totStatus,
        ProjectTotRequestDecisionState? requestState,
        DateOnly? projectCompletedOn = null,
        int? projectCompletedYear = null,
        DateOnly? totMetCompletedOn = null,
        bool? totFirstProductionModelManufactured = null,
        DateOnly? totCompletedOn = null)
        => new(
            ProjectId: projectId,
            ProjectName: $"Project {projectId}",
            SponsoringUnit: "Unit",
            ProjectCompletedOn: projectCompletedOn,
            ProjectCompletedYear: projectCompletedYear,
            TotStatus: totStatus,
            TotStartedOn: null,
            TotStartDatePrecision: PartialDatePrecision.None,
            TotCompletedOn: totCompletedOn,
            TotCompletionDatePrecision: totCompletedOn.HasValue ? PartialDatePrecision.Day : PartialDatePrecision.None,
            TotMetDetails: null,
            TotMetCompletedOn: totMetCompletedOn,
            TotFirstProductionModelManufactured: totFirstProductionModelManufactured,
            TotFirstProductionModelManufacturedOn: null,
            TotLastApprovedBy: null,
            TotLastApprovedOnUtc: null,
            RequestState: requestState,
            RequestedStatus: null,
            RequestedStartedOn: null,
            RequestedStartDatePrecision: PartialDatePrecision.None,
            RequestedCompletedOn: null,
            RequestedCompletionDatePrecision: PartialDatePrecision.None,
            RequestedMetDetails: null,
            RequestedMetCompletedOn: null,
            RequestedFirstProductionModelManufactured: null,
            RequestedFirstProductionModelManufacturedOn: null,
            RequestedBy: null,
            RequestedOnUtc: null,
            DecidedBy: null,
            DecidedOnUtc: null,
            RequestRowVersion: null,
            RequestMetadataAvailable: false,
            LatestExternalRemark: null,
            LatestInternalRemark: null,
            LeadProjectOfficer: null);
}
