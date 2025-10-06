using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Pages.Dashboard;
using ProjectManagement.Services;
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

            var userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            var todo = new StubTodoService();
            var page = new IndexModel(todo, userManager, context)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity())
                    },
                    new RouteData(),
                    new ActionDescriptor()))
            };

            await page.OnGetAsync();

            var ev = Assert.Single(page.UpcomingEvents);
            Assert.Equal("All Hands", ev.Title);

            var expected = string.Format(
                CultureInfo.InvariantCulture,
                "{0:dd MMM yyyy} – {1:dd MMM yyyy}",
                DateOnly.FromDateTime(startLocal.DateTime),
                DateOnly.FromDateTime(endLocalExclusive.AddDays(-1).DateTime));

            Assert.Equal(expected, ev.When);
        }

        private sealed class StubTodoService : ITodoService
        {
            public Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20) =>
                Task.FromResult(new TodoWidgetResult());

            public Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null, TodoPriority priority = TodoPriority.Normal, bool pinned = false) =>
                throw new NotImplementedException();

            public Task<bool> ToggleDoneAsync(string ownerId, Guid id, bool done) =>
                throw new NotImplementedException();

            public Task<bool> EditAsync(string ownerId, Guid id, string? title = null, DateTimeOffset? dueAtLocal = null, TodoPriority? priority = null, bool? pinned = null) =>
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
    }
}
