using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.Dashboard.Components.ProjectPulse;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Dashboard;
using Xunit;

namespace ProjectManagement.Tests.Services
{
    public class ProjectPulseServiceTests
    {
        // SECTION: Aggregation correctness
        [Fact]
        public async Task GetAsync_ComputesOngoingBreakdownAndTotals()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var context = new ApplicationDbContext(options);

            var parentAviation = new ProjectCategory { Name = "Aviation", CreatedByUserId = "seed" };
            var parentNaval = new ProjectCategory { Name = "naval operations", CreatedByUserId = "seed" };
            var childUav = new ProjectCategory { Name = "UAV", Parent = parentAviation, CreatedByUserId = "seed" };
            var childSystems = new ProjectCategory { Name = "Systems", Parent = parentNaval, CreatedByUserId = "seed" };

            context.ProjectCategories.AddRange(parentAviation, parentNaval, childUav, childSystems);

            context.Projects.AddRange(
                new Project { Name = "Recon Drone", CreatedByUserId = "seed", Category = childUav },
                new Project { Name = "Trainer", CreatedByUserId = "seed", Category = childUav },
                new Project { Name = "Naval Comms", CreatedByUserId = "seed", Category = childSystems },
                new Project { Name = "Fleet Link", CreatedByUserId = "seed", Category = childSystems },
                new Project { Name = "Archived", CreatedByUserId = "seed", Category = childUav, IsArchived = true },
                new Project
                {
                    Name = "Completed",
                    CreatedByUserId = "seed",
                    Category = childUav,
                    LifecycleStatus = ProjectLifecycleStatus.Completed
                },
                new Project { Name = "Deleted", CreatedByUserId = "seed", Category = childUav, IsDeleted = true }
            );

            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new ProjectPulseService(context, cache, NullLogger<ProjectPulseService>.Instance);

            var result = await service.GetAsync(CancellationToken.None);

            Assert.Equal(6, result.TotalProjects); // excludes deleted only
            Assert.Equal(1, result.CompletedCount);
            Assert.Equal(4, result.OngoingCount); // excludes archived and deleted
            Assert.Equal(result.OngoingByCategory.Sum(x => x.ProjectCount), result.TotalOngoingProjects);

            var ordered = result.OngoingByCategory.ToArray();
            Assert.Equal("Aviation", ordered[0].CategoryName); // alphabetical tie-breaker
            Assert.Equal(2, ordered[0].ProjectCount);
            Assert.Equal("naval operations", ordered[1].CategoryName);
            Assert.Equal(2, ordered[1].ProjectCount);
        }
    }
}
