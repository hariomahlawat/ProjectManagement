using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
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
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class UserMentionApiTests
{
    [Fact]
    public async Task SearchMentions_ReturnsActiveUsersMatchingQuery()
    {
        using var factory = new MentionApiFactory();
        var client = await CreateClientForUserAsync(factory, "searcher", "Searcher");

        await SeedUserAsync(factory, new ApplicationUser
        {
            Id = "mention-one",
            UserName = "mention.one@test.local",
            NormalizedUserName = "MENTION.ONE@TEST.LOCAL",
            Email = "mention.one@test.local",
            NormalizedEmail = "MENTION.ONE@TEST.LOCAL",
            FullName = "Mention One",
            SecurityStamp = Guid.NewGuid().ToString()
        });

        await SeedUserAsync(factory, new ApplicationUser
        {
            Id = "other-user",
            UserName = "other.user@test.local",
            NormalizedUserName = "OTHER.USER@TEST.LOCAL",
            Email = "other.user@test.local",
            NormalizedEmail = "OTHER.USER@TEST.LOCAL",
            FullName = "Other User",
            SecurityStamp = Guid.NewGuid().ToString()
        });

        var response = await client.GetAsync("/api/users/mentions?q=mention&limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<MentionResultDto[]>(SerializerOptions);
        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal("mention-one", results![0].Id);
        Assert.Equal("Mention One", results[0].DisplayName);
    }

    [Fact]
    public async Task SearchMentions_ExcludesDisabledOrPendingUsers()
    {
        using var factory = new MentionApiFactory();
        var client = await CreateClientForUserAsync(factory, "searcher", "Searcher");

        await SeedUserAsync(factory, new ApplicationUser
        {
            Id = "active-user",
            UserName = "active.user@test.local",
            NormalizedUserName = "ACTIVE.USER@TEST.LOCAL",
            Email = "active.user@test.local",
            NormalizedEmail = "ACTIVE.USER@TEST.LOCAL",
            FullName = "Active User",
            SecurityStamp = Guid.NewGuid().ToString()
        });

        await SeedUserAsync(factory, new ApplicationUser
        {
            Id = "disabled-user",
            UserName = "disabled.user@test.local",
            NormalizedUserName = "DISABLED.USER@TEST.LOCAL",
            Email = "disabled.user@test.local",
            NormalizedEmail = "DISABLED.USER@TEST.LOCAL",
            FullName = "Disabled User",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsDisabled = true
        });

        await SeedUserAsync(factory, new ApplicationUser
        {
            Id = "pending-user",
            UserName = "pending.user@test.local",
            NormalizedUserName = "PENDING.USER@TEST.LOCAL",
            Email = "pending.user@test.local",
            NormalizedEmail = "PENDING.USER@TEST.LOCAL",
            FullName = "Pending User",
            SecurityStamp = Guid.NewGuid().ToString(),
            PendingDeletion = true
        });

        var response = await client.GetAsync("/api/users/mentions?q=user&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<MentionResultDto[]>(SerializerOptions);
        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal("active-user", results![0].Id);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static async Task<HttpClient> CreateClientForUserAsync(MentionApiFactory factory, string userId, string fullName)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);

        await SeedUserAsync(factory, new ApplicationUser
        {
            Id = userId,
            UserName = userId,
            NormalizedUserName = userId.ToUpperInvariant(),
            Email = $"{userId}@test.local",
            NormalizedEmail = $"{userId}@TEST.LOCAL",
            FullName = fullName,
            SecurityStamp = Guid.NewGuid().ToString()
        });

        return client;
    }

    private static async Task SeedUserAsync(MentionApiFactory factory, ApplicationUser user)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await userManager.FindByIdAsync(user.Id);
        if (existing is null)
        {
            var result = await userManager.CreateAsync(user);
            Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(e => e.Description)));
        }
        else
        {
            existing.FullName = user.FullName;
            existing.IsDisabled = user.IsDisabled;
            existing.PendingDeletion = user.PendingDeletion;
            existing.Email = user.Email;
            existing.UserName = user.UserName;
            existing.NormalizedEmail = user.NormalizedEmail;
            existing.NormalizedUserName = user.NormalizedUserName;
            await userManager.UpdateAsync(existing);
        }
    }

    private sealed record MentionResultDto(string Id, string DisplayName, string Initials);

    private sealed class MentionApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"mentions-{Guid.NewGuid()}"));
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
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

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId)
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
