using System;
using System.Collections.Generic;
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
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
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
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
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

        [Fact]
        public async Task CreateModel_WhenActiveProject_SetsLifecycleAndTotDefaults()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 9, 30, 0, TimeSpan.Zero));
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Project Orion"
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Projects/Overview", redirect.PageName);

            var project = await context.Projects.Include(p => p.Tot).SingleAsync();
            Assert.False(project.IsLegacy);
            Assert.Equal(ProjectLifecycleStatus.Active, project.LifecycleStatus);
            Assert.Null(project.CompletedOn);
            Assert.Null(project.CompletedYear);
            Assert.NotNull(project.Tot);
            Assert.Equal(ProjectTotStatus.NotStarted, project.Tot!.Status);

            Assert.Empty(context.ProjectIpaFacts);
            Assert.Empty(context.ProjectAonFacts);
            Assert.Empty(context.ProjectBenchmarkFacts);
            Assert.Empty(context.ProjectCommercialFacts);
            Assert.Empty(context.ProjectPncFacts);
            Assert.Empty(context.ProjectSowFacts);
            Assert.Empty(context.ProjectSupplyOrderFacts);
        }

        [Fact]
        public async Task CreateModel_WhenLegacyProjectWithDate_CreatesLifecycleAndFacts()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 9, 30, 0, TimeSpan.Zero));
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Legacy Project",
                    IsLegacy = true,
                    LegacyCompletedOn = new DateTime(2021, 6, 18)
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Projects/Overview", redirect.PageName);

            var project = await context.Projects.Include(p => p.Tot).SingleAsync();
            Assert.True(project.IsLegacy);
            Assert.Equal(ProjectLifecycleStatus.Completed, project.LifecycleStatus);
            Assert.Equal(new DateOnly(2021, 6, 18), project.CompletedOn);
            Assert.Equal(2021, project.CompletedYear);
            Assert.NotNull(project.Tot);
            Assert.Equal(ProjectTotStatus.NotRequired, project.Tot!.Status);

            Assert.Single(context.ProjectIpaFacts);
            Assert.Single(context.ProjectAonFacts);
            Assert.Single(context.ProjectBenchmarkFacts);
            Assert.Single(context.ProjectCommercialFacts);
            Assert.Single(context.ProjectPncFacts);
            var sow = await context.ProjectSowFacts.SingleAsync();
            Assert.Equal("Unspecified", sow.SponsoringUnit);
            Assert.Equal("Unspecified", sow.SponsoringLineDirectorate);
            var supplyOrder = await context.ProjectSupplyOrderFacts.SingleAsync();
            Assert.Equal(new DateOnly(2021, 6, 18), supplyOrder.SupplyOrderDate);
        }

        [Fact]
        public async Task CreateModel_WhenLegacyProjectWithYearOnly_UsesYearForCompletion()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 9, 30, 0, TimeSpan.Zero));
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Legacy Project",
                    IsLegacy = true,
                    LegacyCompletedYear = 2019
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Projects/Overview", redirect.PageName);

            var project = await context.Projects.Include(p => p.Tot).SingleAsync();
            Assert.True(project.IsLegacy);
            Assert.Equal(ProjectLifecycleStatus.Completed, project.LifecycleStatus);
            Assert.Null(project.CompletedOn);
            Assert.Equal(2019, project.CompletedYear);
            Assert.NotNull(project.Tot);
            Assert.Equal(ProjectTotStatus.NotRequired, project.Tot!.Status);

            var supplyOrder = await context.ProjectSupplyOrderFacts.SingleAsync();
            Assert.Equal(new DateOnly(2019, 1, 1), supplyOrder.SupplyOrderDate);
        }

        [Fact]
        public async Task CreateModel_WhenLegacyMissingCompletionDetails_AddsValidationError()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 9, 30, 0, TimeSpan.Zero));
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Legacy Project",
                    IsLegacy = true
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.True(page.ModelState.ContainsKey("Input.LegacyCompletedOn"));
            Assert.Empty(context.Projects);
        }

        [Fact]
        public async Task CreateModel_WhenLegacyHasDateAndYear_AddsValidationError()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 9, 30, 0, TimeSpan.Zero));
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Legacy Project",
                    IsLegacy = true,
                    LegacyCompletedOn = new DateTime(2018, 3, 12),
                    LegacyCompletedYear = 2018
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.True(page.ModelState.ContainsKey("Input.LegacyCompletedYear"));
            Assert.Empty(context.Projects);
        }

        [Fact]
        public async Task CreateModel_WhenLegacyDateIsInFuture_AddsValidationError()
        {
            var (context, userManager) = CreateContextWithIdentity();
            await userManager.CreateAsync(new ApplicationUser { Id = "creator", UserName = "creator" });

            var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 9, 30, 0, TimeSpan.Zero));
            var audit = new NoOpAuditService();
            var page = new CreateModel(context, userManager, clock, audit)
            {
                Input = new CreateModel.InputModel
                {
                    Name = "Legacy Project",
                    IsLegacy = true,
                    LegacyCompletedOn = new DateTime(2025, 1, 1)
                },
                PageContext = BuildPageContext("creator")
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.True(page.ModelState.ContainsKey("Input.LegacyCompletedOn"));
            Assert.Empty(context.Projects);
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

        private sealed class NoOpAuditService : IAuditService
        {
            public Task LogAsync(
                string action,
                string? message = null,
                string level = "Info",
                string? userId = null,
                string? userName = null,
                IDictionary<string, string?>? data = null,
                HttpContext? http = null)
            {
                return Task.CompletedTask;
            }
        }
    }
}
