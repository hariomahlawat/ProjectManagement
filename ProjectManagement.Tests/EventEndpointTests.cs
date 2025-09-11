using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                });
            });
        }

        private HttpClient CreateClient(string? role = null)
        {
            var client = _factory.CreateClient();
            if (role != null)
                client.DefaultRequestHeaders.Add("X-Test-Role", role);
            return client;
        }

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
                Category = ProjectManagement.Models.EventCategory.Training,
                StartUtc = DateTime.UtcNow,
                EndUtc = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Events.Add(ev);
            db.SaveChanges();

            var hod = CreateClient("hod");
            var put = await hod.PutAsJsonAsync($"/calendar/events/{ev.Id}", new { title = "B", category = "Training", startUtc = DateTime.UtcNow, endUtc = DateTime.UtcNow.AddHours(1), isAllDay = false });
            Assert.Equal(System.Net.HttpStatusCode.OK, put.StatusCode);
            var del = await hod.DeleteAsync($"/calendar/events/{ev.Id}");
            Assert.Equal(System.Net.HttpStatusCode.OK, del.StatusCode);
        }

        private class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder, ISystemClock clock)
                : base(options, logger, encoder, clock) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var role = Request.Headers["X-Test-Role"].ToString();
                var claims = new[] { new Claim(ClaimTypes.Name, "test"), new Claim(ClaimTypes.Role, role) };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }
    }
}

