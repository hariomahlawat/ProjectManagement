using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
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

        var result = await service.GetListBootAsync(CancellationToken.None);

        Assert.Equal(2, result.CompletedProjects.Count);
        Assert.Equal(new[] { 1, 2 }, result.CompletedProjects.Select(p => p.Id).ToArray());
        Assert.Equal("Alpha (A-1)", result.CompletedProjects[0].DisplayName);
        Assert.Equal("Beta", result.CompletedProjects[1].DisplayName);
    }
}
