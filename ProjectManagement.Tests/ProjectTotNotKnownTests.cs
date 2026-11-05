using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Redesign PR 3: "Not known" is read everywhere but cannot be recorded yet, so no row can hold
/// it before every running instance can read it (docs/ux/tot.md, Deployment and rollback).
/// </summary>
public sealed class ProjectTotNotKnownTests
{
    [Fact]
    public void Status_IsStoredAsShortText_SoNotKnownNeedsNoMigration()
    {
        using var db = CreateContext();
        foreach (var property in new[]
                 {
                     db.Model.FindEntityType(typeof(ProjectTot))!.FindProperty(nameof(ProjectTot.Status))!,
                     db.Model.FindEntityType(typeof(ProjectTotRequest))!.FindProperty(nameof(ProjectTotRequest.ProposedStatus))!
                 })
        {
            Assert.Equal(typeof(string), property.GetProviderClrType());
            Assert.Equal(32, property.GetMaxLength());
        }

        // EF stores the enum's name, which fits the column.
        Assert.True(nameof(ProjectTotStatus.NotKnown).Length <= 32);
        Assert.Equal(ProjectTotStatus.NotKnown, Enum.Parse<ProjectTotStatus>("NotKnown"));
    }

    [Fact]
    public void Labels_AndCard_ReadNotKnown()
    {
        Assert.Equal("Not known", ProjectTotPresentation.StatusLabel(ProjectTotStatus.NotKnown));

        var card = ProjectTotPresentation.BuildCard(
            ProjectTotStatus.NotKnown, null, PartialDatePrecision.None, null, PartialDatePrecision.None, null, canManage: true);
        Assert.Equal("Not known", card.Title);
        Assert.Equal("Not known whether ToT took place", card.Summary);
        Assert.Equal("neutral", card.Tone);
    }

    [Fact]
    public void NotKnown_IsNotOfferedOrRecordableYet()
    {
        Assert.DoesNotContain(ProjectTotPresentation.StatusOptions, option => option.Status == ProjectTotStatus.NotKnown);
        Assert.False(ProjectTotPresentation.IsRecordable(ProjectTotStatus.NotKnown));
        Assert.True(ProjectTotPresentation.IsRecordable(ProjectTotStatus.NotStarted));
        Assert.True(ProjectTotPresentation.IsRecordable(ProjectTotStatus.Completed));
    }

    [Fact]
    public async Task UpdateAndSubmit_RefuseNotKnown()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 70,
            Name = "Old simulator",
            CreatedAt = new DateTime(2004, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot { ProjectId = 70, Status = ProjectTotStatus.NotStarted }
        });
        await db.SaveChangesAsync();

        var service = new ProjectTotService(db, new FixedClock(new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero)));
        var request = new ProjectTotUpdateRequest(
            ProjectTotStatus.NotKnown, null, PartialDatePrecision.None, null, PartialDatePrecision.None, null, null, null, null);

        var update = await service.UpdateAsync(70, request, "hod");
        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, update.Status);

        var submit = await service.SubmitRequestAsync(70, request, "project-office");
        Assert.Equal(ProjectTotRequestActionStatus.ValidationFailed, submit.Status);

        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.NotStarted, (await db.ProjectTots.SingleAsync(t => t.ProjectId == 70)).Status);
        Assert.False(await db.ProjectTotRequests.AnyAsync(r => r.ProjectId == 70));
    }

    [Fact]
    public async Task Tracker_ReadsAndFiltersNotKnown()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            new Project
            {
                Id = 71,
                Name = "HE 36 Grenade Sml",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot { ProjectId = 71, Status = ProjectTotStatus.NotKnown }
            },
            new Project
            {
                Id = 72,
                Name = "BMP-II Sml",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot { ProjectId = 72, Status = ProjectTotStatus.NotStarted }
            });
        await db.SaveChangesAsync();

        var tracker = new ProjectTotTrackerReadService(db);

        var all = await tracker.GetAsync(new ProjectTotTrackerFilter());
        Assert.Equal(ProjectTotStatus.NotKnown, all.Single(r => r.ProjectId == 71).TotStatus);

        var notKnown = await tracker.GetAsync(new ProjectTotTrackerFilter { TotStatus = ProjectTotStatus.NotKnown });
        Assert.Equal(71, Assert.Single(notKnown).ProjectId);
    }

    [Fact]
    public void Summary_CountsNotKnownAsApplicableAndListsItApart()
    {
        var rows = new List<ProjectTotTrackerRow>
        {
            Row(1, ProjectTotStatus.Completed),
            Row(2, ProjectTotStatus.NotStarted),
            Row(3, ProjectTotStatus.NotKnown),
            Row(4, ProjectTotStatus.NotRequired)
        };

        var summary = SummaryModel.TotSummaryViewModel.FromProjects(rows);

        Assert.Equal(3, summary.TotalProjects);
        Assert.Equal(1, summary.NotKnownCount);
        Assert.Equal(3, Assert.Single(summary.NotKnownProjects).ProjectId);
        Assert.Equal(0, summary.InProgressCount);
        Assert.Contains("for 1 project it is not known whether Transfer of Technology took place", summary.Narrative);
    }

    [Fact]
    public void RegisterFacts_CountNotKnownApartAndNeverAsLongWaiting()
    {
        var register = ProjectTotRegisterFactsBuilder.Build(
            new[] { Row(1, ProjectTotStatus.NotKnown, projectCompletedYear: 1998), Row(2, ProjectTotStatus.NotStarted, projectCompletedYear: 1998) },
            new DateOnly(2026, 11, 5));

        Assert.False(register.Projects.Single(p => p.ProjectId == 1).IsLongWaiting);
        Assert.Equal("Not known", register.Projects.Single(p => p.ProjectId == 1).StatusLabel);
        Assert.Equal(2, register.Position.NeedTot);
        Assert.Equal(1, register.Position.NotKnown);
        Assert.Equal(1, register.Position.NotStarted);
        Assert.Equal(1, register.Position.LongWaiting);
    }

    private static ProjectTotTrackerRow Row(int id, ProjectTotStatus status, int? projectCompletedYear = null)
        => new(
            ProjectId: id,
            ProjectName: $"Project {id}",
            SponsoringUnit: null,
            ProjectCompletedOn: null,
            ProjectCompletedYear: projectCompletedYear,
            TotStatus: status,
            TotStartedOn: null,
            TotStartDatePrecision: PartialDatePrecision.None,
            TotCompletedOn: null,
            TotCompletionDatePrecision: PartialDatePrecision.None,
            TotMetDetails: null,
            TotMetCompletedOn: null,
            TotFirstProductionModelManufactured: null,
            TotFirstProductionModelManufacturedOn: null,
            TotLastApprovedBy: null,
            TotLastApprovedOnUtc: null,
            RequestState: null,
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

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : ProjectManagement.Services.IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
