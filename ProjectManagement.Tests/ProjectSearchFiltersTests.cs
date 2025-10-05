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

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
