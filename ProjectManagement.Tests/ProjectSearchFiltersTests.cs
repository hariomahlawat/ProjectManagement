using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests
{
    public class ProjectSearchFiltersTests
    {
        [Fact]
        public async Task Query_By_Name_Finds_Project()
        {
            await using var context = CreateContext();
            var project = new Project
            {
                Name = "Alpha Road Upgrade",
                Description = "",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };
            var other = new Project
            {
                Name = "Beta Housing",
                Description = "",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.AddRange(project, other);
            await context.SaveChangesAsync();

            var query = context.Projects.AsQueryable();
            var filters = new ProjectSearchFilters("alpha", null, null, null);

            var results = await query.ApplyProjectSearch(filters).ToListAsync();

            Assert.Contains(results, p => p.Name == project.Name);
            Assert.DoesNotContain(results, p => p.Name == other.Name);
        }

        [Fact]
        public async Task Query_By_Description_Finds_Project()
        {
            await using var context = CreateContext();
            var project = new Project
            {
                Name = "Harbour Works",
                Description = "Coastal bridge reinforcement",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };
            var other = new Project
            {
                Name = "Airport Terminal",
                Description = "Expansion of terminal facilities",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.AddRange(project, other);
            await context.SaveChangesAsync();

            var query = context.Projects.AsQueryable();
            var filters = new ProjectSearchFilters("bridge", null, null, null);

            var results = await query.ApplyProjectSearch(filters).ToListAsync();

            Assert.Contains(results, p => p.Name == project.Name);
            Assert.DoesNotContain(results, p => p.Name == other.Name);
        }

        [Fact]
        public async Task Query_By_Assigned_User_Finds_Project()
        {
            await using var context = CreateContext();
            var hod = new ApplicationUser
            {
                Id = "hod-1",
                UserName = "hod.smith",
                FullName = "Harriet Smith"
            };
            var po = new ApplicationUser
            {
                Id = "po-1",
                UserName = "po.jones",
                FullName = "Pat Officer"
            };

            context.Users.AddRange(hod, po);

            var project = new Project
            {
                Name = "Schools Programme",
                Description = "",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow,
                HodUserId = hod.Id,
                LeadPoUserId = po.Id
            };
            var other = new Project
            {
                Name = "Parks Revamp",
                Description = "",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.AddRange(project, other);
            await context.SaveChangesAsync();

            var query = context.Projects
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .AsQueryable();

            var filters = new ProjectSearchFilters("pat officer", null, null, null);

            var results = await query.ApplyProjectSearch(filters).ToListAsync();

            Assert.Contains(results, p => p.Name == project.Name);
            Assert.DoesNotContain(results, p => p.Name == other.Name);
        }

        [Fact]
        public async Task Lifecycle_Filter_SelectsCompletedProjects()
        {
            await using var context = CreateContext();

            var active = new Project
            {
                Name = "Active",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "creator"
            };

            var completed = new Project
            {
                Name = "Completed",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                CreatedByUserId = "creator"
            };

            context.Projects.AddRange(active, completed);
            await context.SaveChangesAsync();

            var filters = new ProjectSearchFilters(null, null, null, null, ProjectLifecycleFilter.Completed);

            var results = await context.Projects.ApplyProjectSearch(filters).ToListAsync();

            Assert.Single(results);
            Assert.Equal(ProjectLifecycleStatus.Completed, results[0].LifecycleStatus);
        }

        [Fact]
        public async Task Lifecycle_Filter_SelectsLegacyProjects()
        {
            await using var context = CreateContext();

            var modern = new Project
            {
                Name = "Modern",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "creator"
            };

            var legacy = new Project
            {
                Name = "Legacy",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                IsLegacy = true,
                CreatedAt = DateTime.UtcNow.AddYears(-5),
                CreatedByUserId = "creator"
            };

            context.Projects.AddRange(modern, legacy);
            await context.SaveChangesAsync();

            var filters = new ProjectSearchFilters(null, null, null, null, ProjectLifecycleFilter.Legacy);

            var results = await context.Projects.ApplyProjectSearch(filters).ToListAsync();

            Assert.Single(results);
            Assert.True(results[0].IsLegacy);
        }

        [Fact]
        public async Task CompletedYear_Filter_ReturnsMatchingProjects()
        {
            await using var context = CreateContext();

            var project2023 = new Project
            {
                Name = "Project 2023",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CompletedYear = 2023,
                CreatedAt = DateTime.UtcNow.AddYears(-1),
                CreatedByUserId = "creator"
            };

            var project2024 = new Project
            {
                Name = "Project 2024",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CompletedYear = 2024,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "creator"
            };

            context.Projects.AddRange(project2023, project2024);
            await context.SaveChangesAsync();

            var filters = new ProjectSearchFilters(null, null, null, null, ProjectLifecycleFilter.All, 2024);

            var results = await context.Projects.ApplyProjectSearch(filters).ToListAsync();

            Assert.Single(results);
            Assert.Equal(2024, results[0].CompletedYear);
        }

        [Fact]
        public async Task TotStatus_Filter_ReturnsMatchingProjects()
        {
            await using var context = CreateContext();

            var withTot = new Project
            {
                Name = "Project With Tot",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "creator"
            };
            withTot.Tot = new ProjectTot
            {
                Project = withTot,
                Status = ProjectTotStatus.Completed
            };

            var withoutTot = new Project
            {
                Name = "Project Without Tot",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "creator"
            };
            withoutTot.Tot = new ProjectTot
            {
                Project = withoutTot,
                Status = ProjectTotStatus.NotStarted
            };

            context.Projects.AddRange(withTot, withoutTot);
            await context.SaveChangesAsync();

            var filters = new ProjectSearchFilters(null, null, null, null, ProjectLifecycleFilter.All, null, ProjectTotStatus.Completed);

            var results = await context.Projects.Include(p => p.Tot).ApplyProjectSearch(filters).ToListAsync();

            Assert.Single(results);
            Assert.Equal(ProjectTotStatus.Completed, results[0].Tot!.Status);
        }

        [Fact]
        public async Task Archived_Projects_Are_Excluded_By_Default()
        {
            await using var context = CreateContext();

            var active = new Project
            {
                Name = "Active",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow,
                IsArchived = false
            };

            var archived = new Project
            {
                Name = "Archived",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                IsArchived = true
            };

            context.Projects.AddRange(active, archived);
            await context.SaveChangesAsync();

            var defaultFilters = new ProjectSearchFilters(null, null, null, null);
            var defaultResults = await context.Projects.ApplyProjectSearch(defaultFilters).ToListAsync();

            Assert.Single(defaultResults);
            Assert.Equal(active.Name, defaultResults[0].Name);

            var includeFilters = new ProjectSearchFilters(null, null, null, null, ProjectLifecycleFilter.All, null, null, true);
            var includedResults = await context.Projects.ApplyProjectSearch(includeFilters).ToListAsync();

            Assert.Equal(2, includedResults.Count);
        }

        [Fact]
        public async Task Trashed_Projects_Are_Always_Excluded()
        {
            await using var context = CreateContext();

            var active = new Project
            {
                Name = "Active",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow,
                IsArchived = false,
                IsDeleted = false
            };

            var trashed = new Project
            {
                Name = "Trashed",
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                IsArchived = true,
                IsDeleted = true
            };

            context.Projects.AddRange(active, trashed);
            await context.SaveChangesAsync();

            var defaultFilters = new ProjectSearchFilters(null, null, null, null);
            var defaultResults = await context.Projects.ApplyProjectSearch(defaultFilters).ToListAsync();

            Assert.Single(defaultResults);
            Assert.Equal(active.Name, defaultResults[0].Name);

            var includeFilters = new ProjectSearchFilters(null, null, null, null, ProjectLifecycleFilter.All, null, null, true);
            var includedResults = await context.Projects.ApplyProjectSearch(includeFilters).ToListAsync();

            Assert.Single(includedResults);
            Assert.All(includedResults, p => Assert.False(p.IsDeleted));
        }

        [Fact]
        public async Task CategoryDescendants_Are_Included_WhenProvided()
        {
            await using var context = CreateContext();

            var parent = new ProjectCategory { Name = "Parent" };
            var child = new ProjectCategory { Name = "Child", Parent = parent };
            var other = new ProjectCategory { Name = "Other" };

            context.ProjectCategories.AddRange(parent, child, other);
            await context.SaveChangesAsync();

            context.Projects.AddRange(
                new Project
                {
                    Name = "Parent Project",
                    CategoryId = parent.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = DateTime.UtcNow
                },
                new Project
                {
                    Name = "Child Project",
                    CategoryId = child.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = DateTime.UtcNow
                },
                new Project
                {
                    Name = "Other Project",
                    CategoryId = other.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = DateTime.UtcNow
                });

            await context.SaveChangesAsync();

            var hierarchy = new ProjectCategoryHierarchyService(context);
            var categoryIds = await hierarchy.GetCategoryAndDescendantIdsAsync(parent.Id);

            var filters = new ProjectSearchFilters(
                null,
                parent.Id,
                null,
                null,
                ProjectLifecycleFilter.All,
                null,
                null,
                false,
                null,
                null,
                null,
                true,
                categoryIds);

            var results = await context.Projects.ApplyProjectSearch(filters).ToListAsync();

            Assert.Equal(2, results.Count);
            Assert.Contains(results, p => p.Name == "Parent Project");
            Assert.Contains(results, p => p.Name == "Child Project");
            Assert.DoesNotContain(results, p => p.Name == "Other Project");
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
