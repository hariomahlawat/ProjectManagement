using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Dashboard.Components.OpsSignals;
using ProjectManagement.Areas.Dashboard.Components.ProjectPulse;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Pages.Dashboard;
using ProjectManagement.Services;
using ProjectManagement.Services.Dashboard;
using ProjectManagement.Models.Scheduling;
using Xunit;

namespace ProjectManagement.Tests
{
    public class DashboardPageTests
    {
        [Fact]
        public async Task OnGetAsync_FormatsAllDayMultiDayEventWithRange()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var context = new ApplicationDbContext(options);

            var tz = IstClock.TimeZone;
            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var startDateLocal = nowLocal.Date.AddDays(5);
            var startLocal = new DateTimeOffset(startDateLocal, tz.GetUtcOffset(startDateLocal));
            var endLocalExclusive = startLocal.AddDays(2);

            var startUtc = TimeZoneInfo.ConvertTime(startLocal, TimeZoneInfo.Utc);
            var endUtc = TimeZoneInfo.ConvertTime(endLocalExclusive, TimeZoneInfo.Utc);

            var createdAt = DateTime.UtcNow;

            context.Events.Add(new Event
            {
                Id = Guid.NewGuid(),
                Title = "All Hands",
                Category = EventCategory.Conference,
                StartUtc = startUtc,
                EndUtc = endUtc,
                IsAllDay = true,
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            });
            await context.SaveChangesAsync();

            using var serviceProvider = new ServiceCollection().BuildServiceProvider();

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                serviceProvider,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            var todo = new StubTodoService();
            var projectPulse = new StubProjectPulseService();
            var opsSignals = new StubOpsSignalsService();
            var page = new IndexModel(todo, userManager, context, projectPulse, opsSignals)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity())
                    },
                    new RouteData(),
                    new ActionDescriptor()))
            };

            await page.OnGetAsync(CancellationToken.None);

            var ev = Assert.Single(page.UpcomingEvents);
            Assert.Equal("All Hands", ev.Title);

            var expected = string.Format(
                CultureInfo.InvariantCulture,
                "{0:dd MMM yyyy} – {1:dd MMM yyyy}",
                DateOnly.FromDateTime(startLocal.DateTime),
                DateOnly.FromDateTime(endLocalExclusive.AddDays(-1).DateTime));

            Assert.Equal(expected, ev.When);
        }

        [Fact]
        public async Task OnGetAsync_IncludesUpcomingHolidays()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var context = new ApplicationDbContext(options);

            var tz = IstClock.TimeZone;
            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var holidayDate = DateOnly.FromDateTime(nowLocal.Date.AddDays(3));

            context.Holidays.Add(new Holiday
            {
                Name = "Republic Day",
                Date = holidayDate
            });
            await context.SaveChangesAsync();

            using var serviceProvider = new ServiceCollection().BuildServiceProvider();

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                serviceProvider,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            var todo = new StubTodoService();
            var projectPulse = new StubProjectPulseService();
            var opsSignals = new StubOpsSignalsService();
            var page = new IndexModel(todo, userManager, context, projectPulse, opsSignals)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity())
                    },
                    new RouteData(),
                    new ActionDescriptor()))
            };

            await page.OnGetAsync(CancellationToken.None);

            var ev = Assert.Single(page.UpcomingEvents);
            Assert.Equal("Holiday: Republic Day", ev.Title);
            Assert.True(ev.IsHoliday);

            var expected = holidayDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
            Assert.Equal(expected, ev.When);
        }

        [Fact]
        public async Task OnPostSnoozeAsync_TomorrowMorningAfterTen_UsesNextDay()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var context = new ApplicationDbContext(options);

            using var serviceProvider = new ServiceCollection().BuildServiceProvider();

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                serviceProvider,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            var todo = new RecordingTodoService();
            var nowIst = new DateTimeOffset(2024, 5, 10, 15, 30, 0, TimeSpan.FromHours(5.5));

            var projectPulse = new StubProjectPulseService();
            var opsSignals = new StubOpsSignalsService();
            var page = new TestableIndexModel(todo, userManager, context, projectPulse, opsSignals, nowIst)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "user-1")
                        }, "TestAuth"))
                    },
                    new RouteData(),
                    new ActionDescriptor()))
            };

            var id = Guid.NewGuid();
            var result = await page.OnPostSnoozeAsync(id, "tom_am");

            Assert.IsType<RedirectToPageResult>(result);

            var expected = new DateTimeOffset(nowIst.Date.AddDays(1).AddHours(10), nowIst.Offset);
            Assert.Equal(expected, todo.LastDueAtLocal);
            Assert.Equal(id, todo.LastEditId);
            Assert.True(todo.LastUpdateDueDate);
        }

        [Fact]
        public async Task OnPostSnoozeAsync_ClearPreset_ClearsDueDate()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var context = new ApplicationDbContext(options);

            using var serviceProvider = new ServiceCollection().BuildServiceProvider();

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                serviceProvider,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            var todo = new RecordingTodoService();
            var nowIst = new DateTimeOffset(2024, 5, 10, 15, 30, 0, TimeSpan.FromHours(5.5));

            var projectPulse = new StubProjectPulseService();
            var opsSignals = new StubOpsSignalsService();
            var page = new TestableIndexModel(todo, userManager, context, projectPulse, opsSignals, nowIst)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "user-1")
                        }, "TestAuth"))
                    },
                    new RouteData(),
                    new ActionDescriptor()))
            };

            var id = Guid.NewGuid();
            var result = await page.OnPostSnoozeAsync(id, "clear");

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(id, todo.LastEditId);
            Assert.Null(todo.LastDueAtLocal);
            Assert.True(todo.LastUpdateDueDate);
        }

        private sealed class StubTodoService : ITodoService
        {
            public Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20) =>
                Task.FromResult(new TodoWidgetResult());

            public Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null, TodoPriority priority = TodoPriority.Normal, bool pinned = false) =>
                throw new NotImplementedException();

            public Task<bool> ToggleDoneAsync(string ownerId, Guid id, bool done) =>
                throw new NotImplementedException();

            public Task<bool> EditAsync(string ownerId, Guid id, string? title = null, DateTimeOffset? dueAtLocal = null, bool updateDueDate = false, TodoPriority? priority = null, bool? pinned = null) =>
                throw new NotImplementedException();

            public Task<bool> DeleteAsync(string ownerId, Guid id) =>
                throw new NotImplementedException();

            public Task<int> ClearCompletedAsync(string ownerId) =>
                throw new NotImplementedException();

            public Task<bool> ReorderAsync(string ownerId, IList<Guid> orderedIds) =>
                throw new NotImplementedException();

            public Task MarkDoneAsync(string ownerId, IList<Guid> ids) =>
                throw new NotImplementedException();

            public Task DeleteManyAsync(string ownerId, IList<Guid> ids) =>
                throw new NotImplementedException();
        }

        private sealed class RecordingTodoService : ITodoService
        {
            public Guid? LastEditId { get; private set; }

            public DateTimeOffset? LastDueAtLocal { get; private set; }

            public Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20) =>
                Task.FromResult(new TodoWidgetResult());

            public Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null, TodoPriority priority = TodoPriority.Normal, bool pinned = false) =>
                throw new NotImplementedException();

            public Task<bool> ToggleDoneAsync(string ownerId, Guid id, bool done) =>
                throw new NotImplementedException();

            public bool LastUpdateDueDate { get; private set; }

            public Task<bool> EditAsync(string ownerId, Guid id, string? title = null, DateTimeOffset? dueAtLocal = null, bool updateDueDate = false, TodoPriority? priority = null, bool? pinned = null)
            {
                LastEditId = id;
                LastDueAtLocal = dueAtLocal;
                LastUpdateDueDate = updateDueDate;
                return Task.FromResult(true);
            }

            public Task<bool> DeleteAsync(string ownerId, Guid id) =>
                throw new NotImplementedException();

            public Task<int> ClearCompletedAsync(string ownerId) =>
                throw new NotImplementedException();

            public Task<bool> ReorderAsync(string ownerId, IList<Guid> orderedIds) =>
                throw new NotImplementedException();

            public Task MarkDoneAsync(string ownerId, IList<Guid> ids) =>
                throw new NotImplementedException();

            public Task DeleteManyAsync(string ownerId, IList<Guid> ids) =>
                throw new NotImplementedException();
        }

        private sealed class TestableIndexModel : IndexModel
        {
            private readonly DateTimeOffset _nowIst;

            public TestableIndexModel(
                ITodoService todo,
                UserManager<ApplicationUser> users,
                ApplicationDbContext context,
                IProjectPulseService projectPulse,
                IOpsSignalsService opsSignals,
                DateTimeOffset nowIst)
                : base(todo, users, context, projectPulse, opsSignals)
            {
                _nowIst = nowIst;
            }

            internal override DateTimeOffset GetNowIst() => _nowIst;
        }

        private sealed class StubProjectPulseService : IProjectPulseService
        {
            public Task<ProjectPulseVm> GetAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new ProjectPulseVm
                {
                    ProliferationEligible = 0,
                    AnalyticsUrl = string.Empty,
                    CompletedCount = 0,
                    OngoingCount = 0,
                    TotalProjects = 0,
                    CompletedByYear = Array.Empty<BarPoint>(),
                    OngoingByProjectCategory = Array.Empty<CategorySlice>(),
                    AllByTechnicalCategoryTop = Array.Empty<CategorySlice>(),
                    RemainingTechCategories = 0,
                    CompletedUrl = string.Empty,
                    OngoingUrl = string.Empty,
                    RepositoryUrl = string.Empty
                });
            }
        }

        private sealed class StubOpsSignalsService : IOpsSignalsService
        {
            public Task<OpsSignalsVm> GetAsync(DateOnly? from, DateOnly? to, string userId, CancellationToken ct)
            {
                return Task.FromResult(new OpsSignalsVm
                {
                    Tiles = Array.Empty<OpsTileVm>(),
                    RangeStart = from,
                    RangeEnd = to
                });
            }
        }
    }
}
