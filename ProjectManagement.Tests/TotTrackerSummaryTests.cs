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

    private static ProjectTotTrackerRow CreateRow(
        int projectId,
        ProjectTotStatus? totStatus,
        ProjectTotRequestDecisionState? requestState,
        DateOnly? totMetCompletedOn = null,
        bool? totFirstProductionModelManufactured = null)
        => new(
            ProjectId: projectId,
            ProjectName: $"Project {projectId}",
            SponsoringUnit: "Unit",
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
            LatestExternalRemark: null,
            LatestInternalRemark: null,
            LeadProjectOfficer: null);
}
