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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Dashboard.Components.OpsSignals;
using ProjectManagement.Areas.Dashboard.Components.ProjectPulse;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Pages.Dashboard;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Dashboard;
using ProjectManagement.Services.Notebook;
using ProjectManagement.Services.Projects;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.ViewModels.Dashboard;
using ProjectManagement.ViewModels.Notebook;
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

            var notebook = new StubNotebookService();
            var projectPulse = new StubProjectPulseService();
            var opsSignals = new StubOpsSignalsService();
            var searchHealth = new StubSearchHealthService();
            var logger = NullLogger<IndexModel>.Instance;
            var page = new IndexModel(notebook, userManager, context, projectPulse, opsSignals, searchHealth, logger)
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

            var notebook = new StubNotebookService();
            var projectPulse = new StubProjectPulseService();
            var opsSignals = new StubOpsSignalsService();
            var searchHealth = new StubSearchHealthService();
            var logger = NullLogger<IndexModel>.Instance;
            var page = new IndexModel(notebook, userManager, context, projectPulse, opsSignals, searchHealth, logger)
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

        private sealed class StubNotebookService : INotebookService
        {
            public Task<NotebookIndexVm> GetIndexAsync(string ownerId, string view, string? query, string? filter, string? tag, Guid? selectedId, CancellationToken ct = default) =>
                Task.FromResult(new NotebookIndexVm());

            public Task<NotebookWidgetVm> GetWidgetAsync(string ownerId, int take = 5, CancellationToken ct = default) =>
                Task.FromResult(new NotebookWidgetVm());

            public Task<IReadOnlyDictionary<string, int>> GetCountsAsync(string ownerId, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

            public Task<Guid> QuickCaptureAsync(string ownerId, string input, NotebookItemType? forcedType = null, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<Guid> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookEditInput input, Guid expectedVersion, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task ArchiveAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task RestoreAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task ReopenAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task DeleteAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task TogglePinAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<NotebookItemDetailVm> SetPinnedAsync(string ownerId, Guid id, bool isPinned, Guid expectedVersion, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<NotebookItemDetailVm?> GetDetailAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task ToggleFavoriteAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task CompleteAsync(string ownerId, Guid id, bool isComplete, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<NotebookItemDetailVm> ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, Guid expectedVersion, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<Guid> DuplicateAsync(string ownerId, Guid id, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task ToggleChecklistItemAsync(string ownerId, int checklistItemId, bool isDone, CancellationToken ct = default) =>
                throw new NotImplementedException();

            public Task<NotebookItemDetailVm> ToggleChecklistItemAsync(string ownerId, Guid itemId, int checklistItemId, bool isDone, Guid expectedVersion, CancellationToken ct = default) =>
                throw new NotImplementedException();
        }

        private sealed class StubSearchHealthService : ISearchHealthService
        {
            public Task<SearchHealthVm> GetAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new SearchHealthVm());
            }
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
                    CompletedUniqueCount = 0,
                    CompletedRebuildCount = 0,
                    OngoingCount = 0,
                    TotalProjects = 0,
                    OngoingStageDistributionTotal = new StageDistributionResult(
                        Array.Empty<StageDistributionItem>(),
                        ProjectLifecycleFilter.Active),
                    OngoingStageDistributionByCategory = Array.Empty<OngoingStageDistributionCategoryVm>(),
                    OngoingBucketsByKey = new Dictionary<string, OngoingBucketSetVm>
                    {
                        ["total"] = new OngoingBucketSetVm(0, 0, 0, 0, 0, 0)
                    },
                    OngoingBucketFilters = new[]
                    {
                        new OngoingBucketFilterVm("total", "All Projects", 0)
                    },
                    AllByTechnicalCategoryTop = Array.Empty<CategorySlice>(),
                    RemainingTechCategories = 0,
                    UniqueCompletedByTechnicalCategory = Array.Empty<TreemapNode>(),
                    UniqueCompletedByProjectType = Array.Empty<TreemapNode>(),
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
