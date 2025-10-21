using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public class ProliferationManageServiceTests
{
    [Fact]
    public async Task GetListBootAsync_ReturnsCompletedProjectsWithDisplayNames()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "Alpha",
                CaseFileNumber = "A-1",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedByUserId = "creator",
                RowVersion = new byte[] { 1 }
            },
            new Project
            {
                Id = 2,
                Name = "Beta",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedByUserId = "creator",
                RowVersion = new byte[] { 1 }
            });

        await context.SaveChangesAsync();

        var service = new ProliferationManageService(context);

        var result = await service.GetListBootAsync(null, null, null, null, CancellationToken.None);

        Assert.Equal(2, result.CompletedProjects.Count);
        Assert.Equal(new[] { 1, 2 }, result.CompletedProjects.Select(p => p.Id).ToArray());
        Assert.Equal("Alpha (A-1)", result.CompletedProjects[0].DisplayName);
        Assert.Equal("Beta", result.CompletedProjects[1].DisplayName);
        Assert.Equal(new ProliferationManageBootDefaults(null, null, null, null), result.Defaults);
    }

    [Fact]
    public async Task GetListBootAsync_NormalizesDefaults()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Projects.Add(new Project
        {
            Id = 3,
            Name = "Gamma",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CreatedByUserId = "creator",
            RowVersion = new byte[] { 1 }
        });

        await context.SaveChangesAsync();

        var service = new ProliferationManageService(context);

        var valid = await service.GetListBootAsync(3, ProliferationSource.Sdd, 2025, ProliferationRecordKind.Yearly, CancellationToken.None);
        Assert.Equal(new ProliferationManageBootDefaults(3, ProliferationSource.Sdd, 2025, ProliferationRecordKind.Yearly), valid.Defaults);

        var invalid = await service.GetListBootAsync(-1, (ProliferationSource)99, 1999, (ProliferationRecordKind)42, CancellationToken.None);
        Assert.Equal(new ProliferationManageBootDefaults(null, null, null, null), invalid.Defaults);
    }
}
