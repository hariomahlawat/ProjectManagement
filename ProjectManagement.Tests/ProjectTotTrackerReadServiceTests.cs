using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotTrackerReadServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsLatestExternalAndInternalRemarks()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Orion",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 1,
                Status = ProjectTotStatus.Completed,
                StartedOn = new DateOnly(2023, 1, 1),
                CompletedOn = new DateOnly(2023, 12, 31)
            }
        });

        context.Remarks.AddRange(
            new Remark
            {
                ProjectId = 1,
                AuthorUserId = "internal-1",
                AuthorRole = RemarkActorRole.ProjectOfficer,
                Type = RemarkType.Internal,
                Scope = RemarkScope.TransferOfTechnology,
                Body = "Internal note",
                EventDate = new DateOnly(2023, 10, 15),
                CreatedAtUtc = new DateTime(2023, 10, 20, 8, 0, 0, DateTimeKind.Utc)
            },
            new Remark
            {
                ProjectId = 1,
                AuthorUserId = "external-1",
                AuthorRole = RemarkActorRole.ProjectOffice,
                Type = RemarkType.External,
                Scope = RemarkScope.TransferOfTechnology,
                Body = "External summary",
                EventDate = new DateOnly(2023, 11, 10),
                CreatedAtUtc = new DateTime(2023, 11, 12, 9, 30, 0, DateTimeKind.Utc)
            },
            new Remark
            {
                ProjectId = 1,
                AuthorUserId = "external-2",
                AuthorRole = RemarkActorRole.ProjectOffice,
                Type = RemarkType.External,
                Scope = RemarkScope.TransferOfTechnology,
                Body = "Earlier external",
                EventDate = new DateOnly(2023, 8, 5),
                CreatedAtUtc = new DateTime(2023, 8, 6, 10, 0, 0, DateTimeKind.Utc)
            });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);
        var row = Assert.Single(rows);

        Assert.Equal("Project Orion", row.ProjectName);
        Assert.Equal(ProjectTotStatus.Completed, row.TotStatus);
        Assert.Equal("External summary", row.LatestExternalRemark?.Body);
        Assert.Equal(new DateOnly(2023, 11, 10), row.LatestExternalRemark?.EventDate);
        Assert.Equal("Internal note", row.LatestInternalRemark?.Body);
    }

    [Fact]
    public async Task GetAsync_HandlesProjectsWithoutExternalRemarks()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 2,
            Name = "Project Nova",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 2,
                Status = ProjectTotStatus.InProgress,
                StartedOn = new DateOnly(2024, 1, 5)
            }
        });

        context.Remarks.Add(new Remark
        {
            ProjectId = 2,
            AuthorUserId = "internal-2",
            AuthorRole = RemarkActorRole.ProjectOfficer,
            Type = RemarkType.Internal,
            Scope = RemarkScope.TransferOfTechnology,
            Body = "Internal progress",
            EventDate = new DateOnly(2024, 2, 1),
            CreatedAtUtc = new DateTime(2024, 2, 2, 6, 45, 0, DateTimeKind.Utc)
        });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);
        var row = Assert.Single(rows.Where(r => r.ProjectId == 2));

        Assert.Null(row.LatestExternalRemark);
        Assert.Equal("Internal progress", row.LatestInternalRemark?.Body);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
