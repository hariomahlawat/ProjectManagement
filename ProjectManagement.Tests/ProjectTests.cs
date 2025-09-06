using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests
{
    public class ProjectTests
    {
        [Fact]
        public void CanAddProjectToContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var project = new Project { Name = "Test", Description = "Test project" };
            context.Projects.Add(project);
            context.SaveChanges();

            Assert.Equal(1, context.Projects.Count());
        }
    }
}
