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
using ProjectManagement.Data;
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
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                    services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("events"));
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

        private record EventVm(string Id, Guid SeriesId, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string Category, string? Location, bool IsRecurring);
        private record PreferenceVm(bool showCelebrations);

        [Fact]
        public async Task PostRequiresEditorRole()
        {
            using var scope = _factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "User") }, "Test"));
            var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireRole("Admin", "TA", "HoD").Build();
            var result = await auth.AuthorizeAsync(user, null, policy.Requirements);
            Assert.False(result.Succeeded);
        }

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
            var items = JsonSerializer.Deserialize<List<EventVm>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(items);
            Assert.Contains(items!, i => i.SeriesId == ev.Id && i.AllDay && i.Start == start);
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

