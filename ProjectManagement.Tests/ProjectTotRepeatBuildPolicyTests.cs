using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotRepeatBuildPolicyTests
{
    [Fact]
    public void ApplicabilityPolicy_RepeatBuild_IsNotApplicable()
    {
        var project = CreateCompletedProject(1, "Repeat build", isBuild: true);

        Assert.False(ProjectTotApplicabilityPolicy.IsApplicable(project));
        Assert.Equal(
            "Transfer of Technology is not applicable to Repeat Build projects.",
            ProjectTotApplicabilityPolicy.GetIneligibilityReason(project));
    }

    [Fact]
    public void ApplicabilityPolicy_CompletedOriginalProject_IsApplicable()
    {
        var project = CreateCompletedProject(1, "Original", isBuild: false);

        Assert.True(ProjectTotApplicabilityPolicy.IsApplicable(project));
        Assert.Null(ProjectTotApplicabilityPolicy.GetIneligibilityReason(project));
    }

    [Fact]
    public async Task Tracker_ExcludesRepeatBuild_FromOperationalTotUniverse()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            CreateCompletedProject(1, "Original", isBuild: false),
            CreateCompletedProject(2, "Repeat build", isBuild: true));
        await db.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(db);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.ProjectId);
        Assert.Equal("Original", row.ProjectName);
    }




    [Fact]
    public async Task UpdateAsync_RepeatBuild_RejectsFurtherTotChanges()
    {
        await using var db = CreateContext();
        var project = CreateCompletedProject(10, "Repeat build", isBuild: true);
        project.Tot = new ProjectTot
        {
            ProjectId = project.Id,
            Status = ProjectTotStatus.NotStarted
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new ProjectTotService(db, Clock());
        var result = await service.UpdateAsync(
            project.Id,
            CreateRequest(ProjectTotStatus.NotRequired),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Contains("Repeat Build", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectTotStatus.NotStarted, project.Tot.Status);
    }

    [Fact]
    public async Task SubmitRequestAsync_RepeatBuild_DoesNotCreateRequest()
    {
        await using var db = CreateContext();
        var project = CreateCompletedProject(11, "Repeat build", isBuild: true);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new ProjectTotService(db, Clock());
        var result = await service.SubmitRequestAsync(
            project.Id,
            CreateRequest(ProjectTotStatus.NotRequired),
            "project-officer");

        Assert.Equal(ProjectTotRequestActionStatus.ValidationFailed, result.Status);
        Assert.False(await db.ProjectTotRequests.AnyAsync(item => item.ProjectId == project.Id));
    }


    private static Project CreateCompletedProject(int id, string name, bool isBuild) => new()
    {
        Id = id,
        Name = name,
        CreatedAt = new DateTime(2026, 1, 1),
        CreatedByUserId = "creator",
        LifecycleStatus = ProjectLifecycleStatus.Completed,
        IsBuild = isBuild
    };

    private static ProjectTotUpdateRequest CreateRequest(ProjectTotStatus status) => new(
        status,
        StartedOn: null,
        StartDatePrecision: PartialDatePrecision.None,
        CompletedOn: null,
        CompletionDatePrecision: PartialDatePrecision.None,
        MetDetails: null,
        MetCompletedOn: null,
        FirstProductionModelManufactured: null,
        FirstProductionModelManufacturedOn: null);

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IClock Clock() =>
        new FixedClock(new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero));

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
