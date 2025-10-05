using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects;
using Xunit;

namespace ProjectManagement.Tests
{
    public class ProjectIndexPageTests
    {
        [Fact]
        public async Task Pagination_SurfacesOlderProjectsOnLaterPages()
        {
            await using var context = CreateContext();
            var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 0; i < 25; i++)
            {
                context.Projects.Add(new Project
                {
                    Name = $"Project {i:D2}",
                    CreatedAt = baseDate.AddDays(i),
                    CreatedByUserId = "creator"
                });
            }

            await context.SaveChangesAsync();

            var firstPage = new IndexModel(context)
            {
                Page = 1,
                PageSize = 10
            };
            await firstPage.OnGetAsync();

            var secondPage = new IndexModel(context)
            {
                Page = 2,
                PageSize = 10
            };
            await secondPage.OnGetAsync();

            Assert.Equal(10, firstPage.Projects.Count);
            Assert.Equal(10, secondPage.Projects.Count);
            Assert.True(firstPage.Projects.First().CreatedAt > secondPage.Projects.First().CreatedAt);

            var thirdPage = new IndexModel(context)
            {
                Page = 3,
                PageSize = 10
            };
            await thirdPage.OnGetAsync();

            Assert.Equal(5, thirdPage.Projects.Count);
            Assert.Contains(thirdPage.Projects, p => p.Name == "Project 00");
        }

        [Fact]
        public async Task Search_ReordersResultsByRelevance()
        {
            await using var context = CreateContext();
            var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var caseMatch = new Project
            {
                Name = "Legacy capital works",
                CaseFileNumber = "ALPHA100",
                CreatedAt = baseDate.AddDays(1),
                CreatedByUserId = "creator"
            };
            var nameOlder = new Project
            {
                Name = "Alpha100 legacy",
                CreatedAt = baseDate.AddDays(2),
                CreatedByUserId = "creator"
            };
            var nameNewer = new Project
            {
                Name = "Alpha100 expansion",
                CreatedAt = baseDate.AddDays(3),
                CreatedByUserId = "creator"
            };
            var unrelated = new Project
            {
                Name = "Harbour renewal",
                CreatedAt = baseDate.AddDays(4),
                CreatedByUserId = "creator"
            };

            context.Projects.AddRange(caseMatch, nameOlder, nameNewer, unrelated);
            await context.SaveChangesAsync();

            var model = new IndexModel(context)
            {
                Query = "alpha100",
                PageSize = 10
            };

            await model.OnGetAsync();

            Assert.Equal(3, model.Projects.Count);
            Assert.Equal(caseMatch.Id, model.Projects[0].Id);
            Assert.Equal(nameNewer.Id, model.Projects[1].Id);
            Assert.Equal(nameOlder.Id, model.Projects[2].Id);
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
