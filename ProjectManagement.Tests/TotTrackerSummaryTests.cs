using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;
using ProjectManagement.Models;
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
    public void TotSummaryViewModel_PreservesCompletionMetadata()
    {
        var rows = new List<ProjectTotTrackerRow>
        {
            CreateRow(
                projectId: 1,
                totStatus: ProjectTotStatus.Completed,
                requestState: ProjectTotRequestDecisionState.Approved,
                projectCompletedOn: new DateOnly(2024, 1, 15)),
            CreateRow(
                projectId: 2,
                totStatus: ProjectTotStatus.InProgress,
                requestState: ProjectTotRequestDecisionState.Pending,
                projectCompletedYear: 2022,
                totMetCompletedOn: new DateOnly(2024, 2, 1)),
            CreateRow(
                projectId: 3,
                totStatus: ProjectTotStatus.InProgress,
                requestState: null,
                projectCompletedOn: new DateOnly(2024, 3, 1)),
            CreateRow(
                projectId: 4,
                totStatus: ProjectTotStatus.NotStarted,
                requestState: null,
                projectCompletedOn: new DateOnly(2023, 12, 1)),
            CreateRow(
                projectId: 5,
                totStatus: ProjectTotStatus.NotRequired,
                requestState: null,
                projectCompletedYear: 2019)
        };

        var summary = SummaryModel.TotSummaryViewModel.FromProjects(rows);

        Assert.Equal(5, summary.TotalProjects);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.NotStartedCount);
        Assert.Equal(2, summary.InProgressCount);
        Assert.Equal(1, summary.InProgressMetCompleteCount);
        Assert.Equal(1, summary.InProgressMetIncompleteCount);
        Assert.Equal(1, summary.NotRequiredCount);

        var completed = Assert.Single(summary.Completed);
        Assert.Equal(1, completed.ProjectId);
        Assert.Equal(new DateOnly(2024, 1, 15), completed.ProjectCompletedOn);
        Assert.Null(completed.ProjectCompletedYear);

        var notStarted = Assert.Single(summary.NotStarted);
        Assert.Equal(4, notStarted.ProjectId);
        Assert.Equal(new DateOnly(2023, 12, 1), notStarted.ProjectCompletedOn);

        var metComplete = Assert.Single(summary.InProgressMetComplete);
        Assert.Equal(2, metComplete.ProjectId);
        Assert.Null(metComplete.ProjectCompletedOn);
        Assert.Equal(2022, metComplete.ProjectCompletedYear);

        var metIncomplete = Assert.Single(summary.InProgressMetIncomplete);
        Assert.Equal(3, metIncomplete.ProjectId);
        Assert.Equal(new DateOnly(2024, 3, 1), metIncomplete.ProjectCompletedOn);

        var notRequired = Assert.Single(summary.NotRequired);
        Assert.Equal(5, notRequired.ProjectId);
        Assert.Equal(2019, notRequired.ProjectCompletedYear);

        Assert.Equal("15 Jan 2024", SummaryModel.TotSummaryViewModel.FormatCompletionLabel(completed));
        Assert.Equal("2022", SummaryModel.TotSummaryViewModel.FormatCompletionLabel(metComplete));
    }

    private static ProjectTotTrackerRow CreateRow(
        int projectId,
        ProjectTotStatus? totStatus,
        ProjectTotRequestDecisionState? requestState,
        DateOnly? projectCompletedOn = null,
        int? projectCompletedYear = null,
        DateOnly? totMetCompletedOn = null,
        bool? totFirstProductionModelManufactured = null)
        => new(
            ProjectId: projectId,
            ProjectName: $"Project {projectId}",
            SponsoringUnit: "Unit",
            ProjectCompletedOn: projectCompletedOn,
            ProjectCompletedYear: projectCompletedYear,
            TotStatus: totStatus,
            TotStartedOn: null,
            TotCompletedOn: null,
            TotMetDetails: null,
            TotMetCompletedOn: totMetCompletedOn,
            TotFirstProductionModelManufactured: totFirstProductionModelManufactured,
            TotFirstProductionModelManufacturedOn: null,
            TotLastApprovedBy: null,
            TotLastApprovedOnUtc: null,
            RequestState: requestState,
            RequestedStatus: null,
            RequestedStartedOn: null,
            RequestedCompletedOn: null,
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
