using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests
{
    public class ProjectTests
    {
        private static (ApplicationDbContext Context, UserManager<ApplicationUser> UserManager) CreateContextWithIdentity()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var context = new ApplicationDbContext(options);

            var services = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider();

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                new Logger<UserManager<ApplicationUser>>(new LoggerFactory()));

            return (context, userManager);
        }

        [Fact]
        public void CanAddProjectToContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var project = new Project
            {
                Name = "Test",
                Description = "Test project",
                CreatedByUserId = "test-user"
            };
            context.Projects.Add(project);
            context.SaveChanges();

            Assert.Equal(1, context.Projects.Count());
        }

        [Fact]
        public async Task CreateModel_AllowsMissingRoleAssignments()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 12, 0, 0, TimeSpan.Zero));
            var page = new CreateModel(context, userManager, clock)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Project Alpha",
                    Description = "New project"
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Projects/Overview", redirect.PageName);
            var project = Assert.Single(context.Projects);
            Assert.Null(project.HodUserId);
            Assert.Null(project.LeadPoUserId);
            Assert.Null(project.CaseFileNumber);
            Assert.Equal(clock.UtcNow.UtcDateTime, project.CreatedAt);
        }

        [Fact]
        public async Task CreateModel_FlagsDuplicateCaseFileNumber()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            context.Projects.Add(new Project
            {
                Name = "Existing",
                CaseFileNumber = "CF-123",
                CreatedByUserId = "someone",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var clock = new FixedClock(DateTimeOffset.UtcNow);
            var page = new CreateModel(context, userManager, clock)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "New Project",
                    CaseFileNumber = "CF-123"
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.True(page.ModelState.TryGetValue("Input.CaseFileNumber", out var entry));
            Assert.Single(entry!.Errors);
            Assert.Equal(1, context.Projects.Count());
        }

        private static PageContext BuildPageContext(string userId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId)
                }, "TestAuth"))
            };

            return new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        }

        private sealed class FixedClock : IClock
        {
            public FixedClock(DateTimeOffset now)
            {
                UtcNow = now;
            }

            public DateTimeOffset UtcNow { get; }
        }
    }
}
