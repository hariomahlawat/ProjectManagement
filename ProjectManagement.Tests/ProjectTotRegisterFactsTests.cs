using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotRegisterFactsTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Theory]
    [InlineData(1998, true)]
    [InlineData(2020, true)]   // six calendar years ago
    [InlineData(2021, false)]  // five: not more than five
    [InlineData(2026, false)]
    public void NotStarted_IsLongWaiting_WhenTheProjectWasCompletedMoreThanFiveYearsAgo(int projectYear, bool expected)
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.NotStarted, projectCompletedYear: projectYear), Today);

        Assert.Equal(expected, facts.IsLongWaiting);
        Assert.Equal(Today.Year - projectYear, facts.YearsSinceProjectCompleted);
    }

    [Fact]
    public void LongWaiting_NeedsNotStartedAndAKnownProjectYear()
    {
        Assert.False(ProjectTotRegisterFactsBuilder.BuildFacts(Row(ProjectTotStatus.InProgress, projectCompletedYear: 2005), Today).IsLongWaiting);
        Assert.False(ProjectTotRegisterFactsBuilder.BuildFacts(Row(ProjectTotStatus.NotRequired, projectCompletedYear: 2005), Today).IsLongWaiting);

        var noYear = ProjectTotRegisterFactsBuilder.BuildFacts(Row(ProjectTotStatus.NotStarted), Today);
        Assert.False(noYear.IsLongWaiting);
        Assert.Null(noYear.YearsSinceProjectCompleted);
    }

    [Fact]
    public void ProjectCompletedOn_WinsOverTheRecordedYear()
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.NotStarted, projectCompletedOn: new DateOnly(2019, 6, 1), projectCompletedYear: 2023), Today);

        Assert.Equal(2019, facts.ProjectCompletedYear);
        Assert.True(facts.IsLongWaiting);
    }

    [Fact]
    public void NoTotRecord_CountsAsNotStarted()
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(Row(null, projectCompletedYear: 2010), Today);

        Assert.Equal(ProjectTotStatus.NotStarted, facts.Status);
        Assert.Equal("Not started", facts.StatusLabel);
        Assert.True(facts.IsLongWaiting);
    }

    [Fact]
    public void Completed_ReportsDatesAtPrecisionAndYearsFromProjectToTot()
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.Completed,
                projectCompletedYear: 2022,
                completedOn: new DateOnly(2025, 11, 19), completionPrecision: PartialDatePrecision.Month),
            Today);

        Assert.Equal("Nov 2025", facts.Completed);
        Assert.Null(facts.Started);
        Assert.Equal(3, facts.YearsFromProjectToTot);
    }

    [Fact]
    public void DatesSavedBeforePrecision_ShowInFull()
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.Completed,
                startedOn: new DateOnly(2026, 9, 1), startPrecision: PartialDatePrecision.None,
                completedOn: new DateOnly(2026, 10, 15), completionPrecision: PartialDatePrecision.None),
            Today);

        Assert.Equal("01 Sep 2026", facts.Started);
        Assert.Equal("15 Oct 2026", facts.Completed);
    }

    [Fact]
    public void PendingRequest_ListsOnlyTheFieldsItChanges()
    {
        var submittedOn = new DateTime(2026, 11, 3, 5, 10, 0, DateTimeKind.Utc);
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2026, 9, 1), startPrecision: PartialDatePrecision.Day,
                requestState: ProjectTotRequestDecisionState.Pending,
                requestedStatus: ProjectTotStatus.Completed,
                requestedStartedOn: new DateOnly(2026, 9, 1), requestedStartPrecision: PartialDatePrecision.Day,
                requestedCompletedOn: new DateOnly(2026, 10, 31), requestedCompletionPrecision: PartialDatePrecision.Month,
                requestedBy: "K Srinath", requestedOnUtc: submittedOn),
            Today);

        var pending = Assert.IsType<ProjectTotPendingChange>(facts.Pending);
        Assert.Equal(ProjectTotStatus.Completed, pending.ProposedStatus);
        Assert.Equal("K Srinath", pending.SubmittedBy);
        Assert.Equal(submittedOn, pending.SubmittedOnUtc);
        Assert.Equal(
            new[]
            {
                new ProjectTotFieldChange("Status", "In progress", "Completed"),
                new ProjectTotFieldChange("Completed", "—", "Oct 2026")
            },
            pending.Changes);
        Assert.Null(facts.LastDecision);
    }

    [Fact]
    public void PendingRequest_ComparesMilestones()
    {
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.InProgress,
                requestState: ProjectTotRequestDecisionState.Pending,
                requestedStatus: ProjectTotStatus.InProgress,
                requestedMetDetails: "  MET held at the user unit  ",
                requestedMetCompletedOn: new DateOnly(2026, 8, 14),
                requestedFirstProductionModel: false),
            Today);

        Assert.Equal(
            new[]
            {
                new ProjectTotFieldChange("MET details", "—", "MET held at the user unit"),
                new ProjectTotFieldChange("MET completed", "—", "14 Aug 2026"),
                new ProjectTotFieldChange("First production model", "—", "No")
            },
            facts.Pending!.Changes);
    }

    [Fact]
    public void DecidedRequest_IsTheLastDecisionNotAPendingChange()
    {
        var decidedOn = new DateTime(2026, 10, 26, 1, 51, 0, DateTimeKind.Utc);
        var facts = ProjectTotRegisterFactsBuilder.BuildFacts(
            Row(ProjectTotStatus.Completed,
                requestState: ProjectTotRequestDecisionState.Rejected,
                requestedStatus: ProjectTotStatus.NotStarted,
                decidedBy: "Hari Om Ahlawat", decidedOnUtc: decidedOn),
            Today);

        Assert.Null(facts.Pending);
        Assert.Equal(new ProjectTotLastDecision(ProjectTotRequestDecisionState.Rejected, "Hari Om Ahlawat", decidedOn), facts.LastDecision);
    }

    [Fact]
    public void Position_CountsNeedTotWithoutNotRequired()
    {
        var rows = new List<ProjectTotTrackerRow>();
        rows.AddRange(Enumerable.Range(0, 65).Select(_ => Row(ProjectTotStatus.Completed, projectCompletedYear: 2015)));
        rows.AddRange(Enumerable.Range(0, 23).Select(_ => Row(ProjectTotStatus.NotStarted, projectCompletedYear: 2014)));
        rows.AddRange(Enumerable.Range(0, 30).Select(_ => Row(ProjectTotStatus.NotStarted, projectCompletedYear: 2022)));
        rows.AddRange(Enumerable.Range(0, 21).Select(_ => Row(ProjectTotStatus.NotRequired, projectCompletedYear: 2010)));
        rows.Add(Row(ProjectTotStatus.InProgress, requestState: ProjectTotRequestDecisionState.Pending, requestedStatus: ProjectTotStatus.Completed));

        var register = ProjectTotRegisterFactsBuilder.Build(rows, Today);

        Assert.Equal(140, register.Projects.Count);
        Assert.Equal(new ProjectTotPosition(
            NeedTot: 119, Completed: 65, InProgress: 1, NotStarted: 53, NotRequired: 21,
            WaitingForDecision: 1, LongWaiting: 23), register.Position);
        Assert.Equal(55, register.Position.CompletedPercent);
    }

    [Fact]
    public void Position_IsEmptyWithNoProjects()
    {
        var register = ProjectTotRegisterFactsBuilder.Build(Array.Empty<ProjectTotTrackerRow>(), Today);

        Assert.Empty(register.Projects);
        Assert.Equal(ProjectTotPosition.Empty, register.Position);
        Assert.Null(register.Position.CompletedPercent);
    }

    private static int _nextId;

    private static ProjectTotTrackerRow Row(
        ProjectTotStatus? status,
        DateOnly? projectCompletedOn = null,
        int? projectCompletedYear = null,
        DateOnly? startedOn = null,
        PartialDatePrecision startPrecision = PartialDatePrecision.None,
        DateOnly? completedOn = null,
        PartialDatePrecision completionPrecision = PartialDatePrecision.None,
        ProjectTotRequestDecisionState? requestState = null,
        ProjectTotStatus? requestedStatus = null,
        DateOnly? requestedStartedOn = null,
        PartialDatePrecision requestedStartPrecision = PartialDatePrecision.None,
        DateOnly? requestedCompletedOn = null,
        PartialDatePrecision requestedCompletionPrecision = PartialDatePrecision.None,
        string? requestedMetDetails = null,
        DateOnly? requestedMetCompletedOn = null,
        bool? requestedFirstProductionModel = null,
        string? requestedBy = null,
        DateTime? requestedOnUtc = null,
        string? decidedBy = null,
        DateTime? decidedOnUtc = null)
    {
        var id = System.Threading.Interlocked.Increment(ref _nextId);
        return new ProjectTotTrackerRow(
            ProjectId: id,
            ProjectName: $"Project {id}",
            SponsoringUnit: null,
            ProjectCompletedOn: projectCompletedOn,
            ProjectCompletedYear: projectCompletedYear,
            TotStatus: status,
            TotStartedOn: startedOn,
            TotStartDatePrecision: startPrecision,
            TotCompletedOn: completedOn,
            TotCompletionDatePrecision: completionPrecision,
            TotMetDetails: null,
            TotMetCompletedOn: null,
            TotFirstProductionModelManufactured: null,
            TotFirstProductionModelManufacturedOn: null,
            TotLastApprovedBy: null,
            TotLastApprovedOnUtc: null,
            RequestState: requestState,
            RequestedStatus: requestedStatus,
            RequestedStartedOn: requestedStartedOn,
            RequestedStartDatePrecision: requestedStartPrecision,
            RequestedCompletedOn: requestedCompletedOn,
            RequestedCompletionDatePrecision: requestedCompletionPrecision,
            RequestedMetDetails: requestedMetDetails,
            RequestedMetCompletedOn: requestedMetCompletedOn,
            RequestedFirstProductionModelManufactured: requestedFirstProductionModel,
            RequestedFirstProductionModelManufacturedOn: null,
            RequestedBy: requestedBy,
            RequestedOnUtc: requestedOnUtc,
            DecidedBy: decidedBy,
            DecidedOnUtc: decidedOnUtc,
            RequestRowVersion: null,
            RequestMetadataAvailable: requestState is not null,
            LatestExternalRemark: null,
            LatestInternalRemark: null,
            LeadProjectOfficer: null);
    }
}
