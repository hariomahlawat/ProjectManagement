using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
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

            var firstPage = CreateModel(context);
            firstPage.CurrentPage = 1;
            firstPage.PageSize = 10;
            await firstPage.OnGetAsync();

            var secondPage = CreateModel(context);
            secondPage.CurrentPage = 2;
            secondPage.PageSize = 10;
            await secondPage.OnGetAsync();

            Assert.Equal(10, firstPage.Projects.Count);
            Assert.Equal(10, secondPage.Projects.Count);
            Assert.True(firstPage.Projects.First().CreatedAt > secondPage.Projects.First().CreatedAt);

            var thirdPage = CreateModel(context);
            thirdPage.CurrentPage = 3;
            thirdPage.PageSize = 10;
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

            var model = CreateModel(context);
            model.Query = "alpha100";
            model.PageSize = 10;

            await model.OnGetAsync();

            Assert.Equal(3, model.Projects.Count);
            Assert.Equal(caseMatch.Id, model.Projects[0].Id);
            Assert.Equal(nameNewer.Id, model.Projects[1].Id);
            Assert.Equal(nameOlder.Id, model.Projects[2].Id);
        }

        [Fact]
        public async Task OnGet_FiltersProjectsByLifecycle()
        {
            await using var context = CreateContext();

            var active = new Project
            {
                Name = "Active",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            var completed = new Project
            {
                Name = "Completed",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.AddRange(active, completed);
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.Lifecycle = ProjectLifecycleFilter.Completed;

            await model.OnGetAsync();

            Assert.Single(model.Projects);
            Assert.All(model.Projects, p => Assert.Equal(ProjectLifecycleStatus.Completed, p.LifecycleStatus));
        }

        [Fact]
        public async Task LifecycleTabs_HighlightSelectedFilter()
        {
            await using var context = CreateContext();

            context.Projects.Add(new Project
            {
                Name = "Only",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.Lifecycle = ProjectLifecycleFilter.Cancelled;

            await model.OnGetAsync();

            Assert.Contains(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.Cancelled && tab.IsActive);
            Assert.Contains(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.All && !tab.IsActive);
        }

        [Fact]
        public async Task LifecycleTabs_DisplayCounts()
        {
            await using var context = CreateContext();

            var now = DateTime.UtcNow;

            context.Projects.AddRange(
                new Project
                {
                    Name = "Active",
                    LifecycleStatus = ProjectLifecycleStatus.Active,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Completed",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Cancelled",
                    LifecycleStatus = ProjectLifecycleStatus.Cancelled,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Legacy",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    IsLegacy = true,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                });

            await context.SaveChangesAsync();

            var model = CreateModel(context);

            await model.OnGetAsync();

            Assert.Equal(4, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.All).Count);
            Assert.Equal(1, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Active).Count);
            Assert.Equal(1, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Completed).Count);
            Assert.Equal(1, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Cancelled).Count);
            Assert.Equal(1, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Legacy).Count);
        }

        [Fact]
        public async Task OnGet_ExcludesTrashedProjects()
        {
            await using var context = CreateContext();

            var now = DateTime.UtcNow;

            context.Projects.AddRange(
                new Project
                {
                    Name = "Active",
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Archived",
                    CreatedByUserId = "creator",
                    CreatedAt = now.AddDays(-1),
                    IsArchived = true
                },
                new Project
                {
                    Name = "Trashed",
                    CreatedByUserId = "creator",
                    CreatedAt = now.AddDays(-2),
                    IsDeleted = true,
                    DeletedAt = now.AddDays(-2)
                });

            await context.SaveChangesAsync();

            var defaultModel = CreateModel(context);
            defaultModel.PageSize = 10;

            await defaultModel.OnGetAsync();

            Assert.Single(defaultModel.Projects);
            Assert.All(defaultModel.Projects, p => Assert.False(p.IsDeleted));

            var includeArchivedModel = CreateModel(context);
            includeArchivedModel.PageSize = 10;
            includeArchivedModel.IncludeArchived = true;

            await includeArchivedModel.OnGetAsync();

            Assert.Equal(2, includeArchivedModel.Projects.Count);
            Assert.Contains(includeArchivedModel.Projects, p => p.IsArchived);
            Assert.DoesNotContain(includeArchivedModel.Projects, p => p.IsDeleted);

            var lifecycleTab = includeArchivedModel.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.All);
            Assert.Equal(2, lifecycleTab.Count);
        }

        [Fact]
        public async Task AnalyticsDrilldown_IncludesCategoryDescendants_WhenFlagged()
        {
            await using var context = CreateContext();

            var parent = new ProjectCategory { Name = "Parent" };
            var child = new ProjectCategory { Name = "Child", Parent = parent };
            var sibling = new ProjectCategory { Name = "Sibling" };

            context.ProjectCategories.AddRange(parent, child, sibling);
            await context.SaveChangesAsync();

            var now = DateTime.UtcNow;

            context.Projects.AddRange(
                new Project
                {
                    Name = "Parent Project",
                    CategoryId = parent.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Child Project",
                    CategoryId = child.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Sibling Project",
                    CategoryId = sibling.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                });

            await context.SaveChangesAsync();

            var withDescendants = CreateModel(context);
            withDescendants.CategoryId = parent.Id;
            withDescendants.IncludeCategoryDescendants = true;
            await withDescendants.OnGetAsync();

            Assert.Equal(2, withDescendants.Projects.Count);
            Assert.Contains(withDescendants.Projects, p => p.Name == "Parent Project");
            Assert.Contains(withDescendants.Projects, p => p.Name == "Child Project");

            var directOnly = CreateModel(context);
            directOnly.CategoryId = parent.Id;
            directOnly.IncludeCategoryDescendants = false;
            await directOnly.OnGetAsync();

            Assert.Single(directOnly.Projects);
            Assert.Equal("Parent Project", directOnly.Projects.Single().Name);
        }

        private static IndexModel CreateModel(ApplicationDbContext context)
        {
            var categories = new ProjectCategoryHierarchyService(context);
            var analytics = new ProjectAnalyticsService(context, new TestClock(), categories);
            return new IndexModel(context, analytics, categories);
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        private sealed class TestClock : IClock
        {
            public DateTimeOffset UtcNow { get; } = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
