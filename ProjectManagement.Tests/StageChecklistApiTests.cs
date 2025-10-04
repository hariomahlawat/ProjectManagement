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
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class StageChecklistApiTests
{
    [Fact]
    public async Task ReorderChecklistItems_SwapsTwoItemsAndPersists()
    {
        using var factory = new StageChecklistApiFactory();
        var client = await CreateClientForUserAsync(factory, "mco-user", "MCO", "MCO");

        var seeded = await SeedChecklistAsync(factory);

        var request = new StageChecklistReorderRequest(
            seeded.TemplateRowVersion,
            new List<StageChecklistReorderItem>
            {
                new(seeded.SecondItem.ItemId, 1, seeded.SecondItem.RowVersion),
                new(seeded.FirstItem.ItemId, 2, seeded.FirstItem.RowVersion)
            });

        var response = await client.PostAsJsonAsync(
            $"/api/processes/{seeded.Version}/stages/{seeded.StageCode}/checklist/reorder",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StageChecklistTemplateDto>();
        Assert.NotNull(body);
        Assert.Collection(
            body!.Items,
            first =>
            {
                Assert.Equal(seeded.SecondItem.ItemId, first.Id);
                Assert.Equal(1, first.Sequence);
            },
            second =>
            {
                Assert.Equal(seeded.FirstItem.ItemId, second.Id);
                Assert.Equal(2, second.Sequence);
            });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItems = await db.StageChecklistItemTemplates
            .AsNoTracking()
            .Where(i => i.TemplateId == seeded.TemplateId)
            .OrderBy(i => i.Sequence)
            .Select(i => new { i.Id, i.Sequence })
            .ToListAsync();

        Assert.Equal(new[] { seeded.SecondItem.ItemId, seeded.FirstItem.ItemId }, storedItems.Select(i => i.Id));
        Assert.Equal(new[] { 1, 2 }, storedItems.Select(i => i.Sequence));
    }

    private static async Task<HttpClient> CreateClientForUserAsync(
        StageChecklistApiFactory factory,
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
        StageChecklistApiFactory factory,
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

    private static async Task<SeededChecklist> SeedChecklistAsync(StageChecklistApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var template = new StageChecklistTemplate
        {
            Version = "SDD-1.0",
            StageCode = "FS",
            Items =
            {
                new StageChecklistItemTemplate { Text = "First", Sequence = 1 },
                new StageChecklistItemTemplate { Text = "Second", Sequence = 2 }
            }
        };

        db.StageChecklistTemplates.Add(template);
        await db.SaveChangesAsync();

        var persisted = await db.StageChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Items)
            .SingleAsync(t => t.Id == template.Id);

        var orderedItems = persisted.Items
            .OrderBy(i => i.Sequence)
            .Select(i => new SeededChecklistItem(i.Id, i.RowVersion, i.Sequence))
            .ToList();

        return new SeededChecklist(
            persisted.Id,
            persisted.Version,
            persisted.StageCode,
            persisted.RowVersion,
            orderedItems[0],
            orderedItems[1]);
    }

    private sealed record SeededChecklist(
        int TemplateId,
        string Version,
        string StageCode,
        byte[] TemplateRowVersion,
        SeededChecklistItem FirstItem,
        SeededChecklistItem SecondItem);

    private sealed record SeededChecklistItem(int ItemId, byte[] RowVersion, int Sequence);

    private sealed class StageChecklistApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"checklists-{Guid.NewGuid()}"));
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
