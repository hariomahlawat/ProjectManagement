using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationAuthorizationTests
{
    [Fact]
    public async Task NonProjectOfficeUser_CanAccessOverviewPage()
    {
        using var factory = new ProliferationAuthorizationFactory();
        var client = await CreateClientForUserAsync(factory, "general-user", "General User");

        var response = await client.GetAsync("/ProjectOfficeReports/Proliferation/Index");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonProjectOfficeUser_CanAccessOverviewApi()
    {
        using var factory = new ProliferationAuthorizationFactory();
        var client = await CreateClientForUserAsync(factory, "general-user", "General User");

        var response = await client.GetAsync("/api/proliferation/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProliferationSubmitPolicy_RemainsRestricted()
    {
        using var factory = new ProliferationAuthorizationFactory();
        var client = await CreateClientForUserAsync(factory, "general-user", "General User");

        var response = await client.GetAsync($"/api/proliferation/granular/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProliferationApprovePolicy_RemainsRestricted()
    {
        using var factory = new ProliferationAuthorizationFactory();
        var client = await CreateClientForUserAsync(factory, "general-user", "General User");

        var payload = new ProliferationYearPreferenceDto
        {
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            Year = DateTime.UtcNow.Year,
            Mode = YearPreferenceMode.Auto
        };

        var response = await client.PostAsJsonAsync("/api/proliferation/year-preference", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpClient> CreateClientForUserAsync(
        ProliferationAuthorizationFactory factory,
        string userId,
        string displayName,
        params string[] roles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        }

        await SeedUserAsync(factory, userId, displayName, roles);
        return client;
    }

    private static async Task SeedUserAsync(
        ProliferationAuthorizationFactory factory,
        string userId,
        string displayName,
        IReadOnlyCollection<string> roles)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var createRole = await roleManager.CreateAsync(new IdentityRole(role));
                Assert.True(createRole.Succeeded, string.Join(",", createRole.Errors.Select(e => e.Description)));
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = userId,
                UserName = userId,
                Email = $"{userId}@test.local",
                FullName = displayName
            };

            var createUser = await userManager.CreateAsync(user);
            Assert.True(createUser.Succeeded, string.Join(",", createUser.Errors.Select(e => e.Description)));
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (!await userManager.IsInRoleAsync(user, role))
            {
                var addRole = await userManager.AddToRoleAsync(user, role);
                Assert.True(addRole.Succeeded, string.Join(",", addRole.Errors.Select(e => e.Description)));
            }
        }
    }

    private sealed class ProliferationAuthorizationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase($"proliferation-auth-{Guid.NewGuid()}"));
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
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing user header."));
            }

            var rolesHeader = Request.Headers["X-Test-Roles"].ToString();
            var roles = string.IsNullOrWhiteSpace(rolesHeader)
                ? Array.Empty<string>()
                : rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, userId)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
