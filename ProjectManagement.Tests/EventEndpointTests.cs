using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests
{
    public class EventEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public EventEndpointTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UsePrismTestInfrastructure("event-endpoints");

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                    services.RemoveAll(typeof(DbContextOptions<MediaLibraryDbContext>));
                    services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("events"));
                    services.AddDbContext<MediaLibraryDbContext>(o => o.UseInMemoryDatabase("events-media"));
                });
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultScheme = "Test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                });
            });
        }

        private HttpClient CreateClient(string? role = null, string? userId = null)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            if (role != null)
                client.DefaultRequestHeaders.Add("X-Test-Role", role);
            if (userId != null)
                client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
            return client;
        }

        private record CalendarEventVm(string Id, Guid SeriesId, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string Category, string? Location, bool IsRecurring, bool IsCelebration, Guid? CelebrationId, string? TaskUrl);
        private record CalendarHolidayVm(string Date, string Name, bool? SkipWeekends, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
        private record PreferenceVm(bool showCelebrations);

        [Fact]
        public async Task GeneralEventWritePolicyRejectsOrdinaryUsers()
        {
            using var scope = _factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
            var user = PrincipalWithRole("User");

            var result = await auth.AuthorizeAsync(
                user,
                resource: null,
                policyName: Policies.Calendar.ManageEvents);

            Assert.False(result.Succeeded);
        }

        [Theory]
        [InlineData(RoleNames.Admin)]
        [InlineData(RoleNames.HoD)]
        [InlineData(RoleNames.Ta)]
        [InlineData(RoleNames.Comdt)]
        [InlineData(RoleNames.Mco)]
        [InlineData(RoleNames.ProjectOfficer)]
        [InlineData(RoleNames.ProjectOffice)]
        [InlineData(RoleNames.ProjectOfficeAlternate)]
        public async Task GeneralEventWritePolicyAllowsConfiguredRoles(string role)
        {
            using var scope = _factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

            var result = await auth.AuthorizeAsync(
                PrincipalWithRole(role),
                resource: null,
                policyName: Policies.Calendar.ManageEvents);

            Assert.True(result.Succeeded);
        }

        [Theory]
        [InlineData(RoleNames.Admin, true)]
        [InlineData(RoleNames.HoD, false)]
        [InlineData(RoleNames.Ta, true)]
        [InlineData(RoleNames.Comdt, false)]
        [InlineData(RoleNames.MainOfficeClerk, true)]
        [InlineData(RoleNames.MainOfficeAlternate, true)]
        [InlineData(RoleNames.Mco, false)]
        [InlineData(RoleNames.ProjectOfficer, false)]
        [InlineData(RoleNames.ProjectOffice, false)]
        [InlineData(RoleNames.ProjectOfficeAlternate, false)]
        public async Task BirthdayPolicyUsesTheRequiredRoleMatrix(string role, bool expected)
        {
            using var scope = _factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

            var result = await auth.AuthorizeAsync(
                PrincipalWithRole(role),
                resource: null,
                policyName: Policies.Calendar.ManageBirthdays);

            Assert.Equal(expected, result.Succeeded);
        }

        [Theory]
        [InlineData(RoleNames.Admin, true)]
        [InlineData(RoleNames.HoD, false)]
        [InlineData(RoleNames.Ta, true)]
        [InlineData(RoleNames.Comdt, false)]
        [InlineData(RoleNames.MainOfficeClerk, true)]
        [InlineData(RoleNames.MainOfficeAlternate, true)]
        [InlineData(RoleNames.Mco, false)]
        [InlineData(RoleNames.ProjectOfficer, false)]
        [InlineData(RoleNames.ProjectOffice, false)]
        [InlineData(RoleNames.ProjectOfficeAlternate, false)]
        public async Task AnniversaryPolicyUsesTheRequiredRoleMatrix(string role, bool expected)
        {
            using var scope = _factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

            var result = await auth.AuthorizeAsync(
                PrincipalWithRole(role),
                resource: null,
                policyName: Policies.Calendar.ManageAnniversaries);

            Assert.Equal(expected, result.Succeeded);
        }

        private static ClaimsPrincipal PrincipalWithRole(string role) =>
            new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test"));

        [Fact]
        public async Task HoDCaseInsensitive()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ev = new ProjectManagement.Models.Event
            {
                Id = Guid.NewGuid(),
                Title = "A",
                Category = ProjectManagement.Models.EventCategory.Visit,
                StartUtc = DateTime.UtcNow,
                EndUtc = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Events.Add(ev);
            db.SaveChanges();

            var hod = CreateClient("HoD");
            var put = await hod.PutAsJsonAsync($"/calendar/events/{ev.Id}", new { title = "B", category = "Visit", startUtc = DateTime.UtcNow, endUtc = DateTime.UtcNow.AddHours(1), isAllDay = false });
            Assert.Equal(System.Net.HttpStatusCode.OK, put.StatusCode);
            var del = await hod.DeleteAsync($"/calendar/events/{ev.Id}");
            Assert.Equal(System.Net.HttpStatusCode.OK, del.StatusCode);
        }

        [Fact]
        public async Task CreateEventAcceptsMinutePrecisionOutsideQuarterHourSlots()
        {
            var client = CreateClient(RoleNames.Admin);
            var start = new DateTimeOffset(2026, 7, 10, 9, 20, 0, TimeSpan.FromHours(5.5));
            var response = await client.PostAsJsonAsync("/calendar/events", new
            {
                title = "Minute precision event",
                description = "Created from the calendar editor",
                category = "Other",
                location = "Conference Room",
                startUtc = start,
                endUtc = start.AddHours(1),
                isAllDay = false,
                recurrenceRule = (string?)null,
                recurrenceUntilUtc = (DateTimeOffset?)null
            });

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            Assert.True(body.Contains("id", StringComparison.OrdinalIgnoreCase), body);
        }

        [Fact]
        public async Task CreateEventRejectsWhitespaceTitle()
        {
            var client = CreateClient(RoleNames.Admin);
            var start = DateTimeOffset.UtcNow.AddHours(1);
            var response = await client.PostAsJsonAsync("/calendar/events", new
            {
                title = "   ",
                category = "Other",
                startUtc = start,
                endUtc = start.AddHours(1),
                isAllDay = false
            });

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Contains("Title is required", StringComparison.OrdinalIgnoreCase), body);
        }

        [Fact]
        public async Task CreateEventRejectsRecurrenceEndBeforeStart()
        {
            var client = CreateClient(RoleNames.Admin);
            var start = DateTimeOffset.UtcNow.AddDays(2);
            var response = await client.PostAsJsonAsync("/calendar/events", new
            {
                title = "Invalid recurring event",
                category = "Conference",
                startUtc = start,
                endUtc = start.AddHours(1),
                isAllDay = false,
                recurrenceRule = "FREQ=WEEKLY;INTERVAL=1;BYDAY=MO",
                recurrenceUntilUtc = start.AddDays(-1)
            });

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Contains("cannot be before", StringComparison.OrdinalIgnoreCase), body);
        }

        [Fact]
        public async Task SoftDeletedEventCannotBeReadOrAddedToTasks()
        {
            var eventId = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Events.Add(new ProjectManagement.Models.Event
                {
                    Id = eventId,
                    Title = "Deleted",
                    Category = ProjectManagement.Models.EventCategory.Other,
                    StartUtc = DateTimeOffset.UtcNow,
                    EndUtc = DateTimeOffset.UtcNow.AddHours(1),
                    IsDeleted = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var client = CreateClient(RoleNames.Admin);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync($"/calendar/events/{eventId}")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/calendar/events/{eventId}/task", null)).StatusCode);
        }

        [Fact]
        public async Task RecurringAllDayEventsReturnedInFutureMonths()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ev = new ProjectManagement.Models.Event
            {
                Id = Guid.NewGuid(),
                Title = "Holiday",
                Category = ProjectManagement.Models.EventCategory.Other,
                StartUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                IsAllDay = true,
                RecurrenceRule = "FREQ=MONTHLY",
                RecurrenceUntilUtc = new DateTime(2024, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Events.Add(ev);
            db.SaveChanges();

            var client = CreateClient("Admin");
            var start = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero);
            var url = $"/calendar/events?start={Uri.EscapeDataString(start.UtcDateTime.ToString("o"))}&end={Uri.EscapeDataString(end.UtcDateTime.ToString("o"))}";
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var items = JsonSerializer.Deserialize<List<CalendarEventVm>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(items);
            var match = Assert.Single(items!.Where(i => i.SeriesId == ev.Id && i.AllDay && i.Start == start));
            Assert.False(match.IsCelebration);
            Assert.Equal($"/calendar/events/{ev.Id}/task", match.TaskUrl);
        }

        [Fact]
        public async Task HolidaysEndpointReturnsConfiguredRowsWithinWindow()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Holidays.Add(new ProjectManagement.Models.Scheduling.Holiday
                {
                    Date = new DateOnly(2024, 12, 25),
                    Name = "Founders Day"
                });
                await db.SaveChangesAsync();
            }

            var client = CreateClient("Admin");
            var start = new DateTimeOffset(new DateTime(2024, 12, 24, 0, 0, 0, DateTimeKind.Utc));
            var end = new DateTimeOffset(new DateTime(2024, 12, 26, 0, 0, 0, DateTimeKind.Utc));
            var url = $"/calendar/events/holidays?start={Uri.EscapeDataString(start.UtcDateTime.ToString("o"))}&end={Uri.EscapeDataString(end.UtcDateTime.ToString("o"))}";
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode, body);

            var items = JsonSerializer.Deserialize<List<CalendarHolidayVm>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(items);
            var holiday = Assert.Single(items!);
            Assert.Equal("Founders Day", holiday.Name);
            Assert.Equal("2024-12-25", holiday.Date);
        }

        [Fact]
        public async Task ShowCelebrationsPreferenceCanBeUpdated()
        {
            var userId = Guid.NewGuid().ToString();

            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser
                {
                    Id = userId,
                    UserName = "calendar-pref@example.com",
                    Email = "calendar-pref@example.com",
                    EmailConfirmed = true,
                    ShowCelebrationsInCalendar = true,
                };

                var createResult = await userManager.CreateAsync(user);
                Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));
            }

            var client = CreateClient("Admin", userId);
            var response = await client.PostAsJsonAsync("/calendar/events/preferences/show-celebrations", new { showCelebrations = false });
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<PreferenceVm>();
            Assert.NotNull(payload);
            Assert.False(payload!.showCelebrations);

            using var verifyScope = _factory.Services.CreateScope();
            var verifier = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed = await verifier.FindByIdAsync(userId);
            Assert.NotNull(refreshed);
            Assert.False(refreshed!.ShowCelebrationsInCalendar);
        }

        [Fact]
        public async Task CelebrationsCanBeIncludedAndTaskEndpointWorks()
        {
            var userId = Guid.NewGuid().ToString();
            var celebrationId = Guid.NewGuid();

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<ApplicationDbContext>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

                var user = new ApplicationUser
                {
                    Id = userId,
                    UserName = "calendar-celebration@example.com",
                    Email = "calendar-celebration@example.com",
                    EmailConfirmed = true,
                    ShowCelebrationsInCalendar = false
                };

                var createResult = await userManager.CreateAsync(user);
                Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));

                db.Celebrations.Add(new Celebration
                {
                    Id = celebrationId,
                    EventType = CelebrationType.Birthday,
                    Name = "Leap Legend",
                    Day = 29,
                    Month = 2,
                    CreatedById = userId,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });

                await db.SaveChangesAsync();
            }

            var client = CreateClient("Admin", userId);
            var start = new DateTimeOffset(2025, 2, 27, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2025, 3, 5, 0, 0, 0, TimeSpan.Zero);
            var url = $"/calendar/events?start={Uri.EscapeDataString(start.UtcDateTime.ToString("o"))}&end={Uri.EscapeDataString(end.UtcDateTime.ToString("o"))}&includeCelebrations=true";
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);

            var items = JsonSerializer.Deserialize<List<CalendarEventVm>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(items);
            var celebration = Assert.Single(items!.Where(i => i.IsCelebration));
            Assert.Equal("Celebration", celebration.Category);
            Assert.Equal(celebrationId, celebration.CelebrationId);
            Assert.True(celebration.AllDay);
            Assert.True(celebration.IsRecurring);
            Assert.Equal($"/calendar/events/celebrations/{celebrationId}/task", celebration.TaskUrl);

            var expectedStart = CelebrationHelpers.ToLocalDateTime(new DateOnly(2025, 2, 28)).ToUniversalTime();
            Assert.Equal(expectedStart, celebration.Start);
            Assert.Equal(expectedStart.AddDays(1), celebration.End);

            var taskResponse = await client.PostAsync(celebration.TaskUrl!, null);
            var taskBody = await taskResponse.Content.ReadAsStringAsync();
            Assert.True(taskResponse.IsSuccessStatusCode, taskBody);
        }

        private class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
                : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var role = Request.Headers["X-Test-Role"].ToString();
                var userId = Request.Headers["X-Test-UserId"].ToString();
                var claims = new List<Claim> { new Claim(ClaimTypes.Name, "test") };
                if (!string.IsNullOrEmpty(role))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
                if (!string.IsNullOrEmpty(userId))
                {
                    claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
                }
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }
    }
}

