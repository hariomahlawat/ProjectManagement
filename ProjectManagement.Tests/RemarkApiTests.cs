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
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Remarks;
using Xunit;

namespace ProjectManagement.Tests;

public class RemarkApiTests
{
    [Fact]
    public async Task CreateAndListRemarksAsync_Succeeds()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 601;
        var client = await CreateClientForUserAsync(factory, "user-po", "Project Officer", "Project Officer");
        await SeedProjectAsync(factory, projectId, leadPoUserId: "user-po");

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "<b>Hello</b>",
            eventDate = new DateOnly(2024, 10, 1),
            stageRef = StageCodes.FS
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(created);
        Assert.Equal("user-po", created!.AuthorUserId);
        Assert.Equal("<b>Hello</b>", created.Body);
        Assert.False(string.IsNullOrWhiteSpace(created.RowVersion));

        var list = await client.GetFromJsonAsync<RemarkListResponseDto>($"/api/projects/{projectId}/remarks", SerializerOptions);
        Assert.NotNull(list);
        Assert.Equal(1, list!.Total);
        Assert.Single(list.Items);
        Assert.Equal("user-po", list.Items[0].AuthorUserId);
        Assert.Equal("<b>Hello</b>", list.Items[0].Body);
    }

    [Fact]
    public async Task EditRemarkAsync_DeniesWhenWindowExpired()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 602;
        var client = await CreateClientForUserAsync(factory, "author", "Author", "Project Officer");
        await SeedProjectAsync(factory, projectId, leadPoUserId: "author");

        var create = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "Initial",
            eventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            stageRef = StageCodes.FS
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(created);

        await SetRemarkCreatedAtAsync(factory, created!.Id, DateTime.UtcNow.AddHours(-4));
        var rowVersion = await GetRemarkRowVersionAsync(factory, created.Id);

        var update = await client.PutAsJsonAsync($"/api/projects/{projectId}/remarks/{created.Id}", new
        {
            body = "Updated",
            eventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            stageRef = StageCodes.FS,
            rowVersion,
            actorRole = RemarkActorRole.ProjectOfficer.ToString()
        });

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        var problem = await update.Content.ReadFromJsonAsync<ProblemDetailsDto>(SerializerOptions);
        Assert.NotNull(problem);
        Assert.Equal(RemarkService.EditWindowMessage, problem!.Title);
    }

    [Fact]
    public async Task DeleteRemarkAsync_ForbidsNonAuthor()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 603;
        var authorClient = await CreateClientForUserAsync(factory, "author", "Author", "Project Officer");
        await SeedProjectAsync(factory, projectId, leadPoUserId: "author");

        var create = await authorClient.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "Body",
            eventDate = new DateOnly(2024, 9, 30),
            stageRef = StageCodes.FS
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(created);

        var intruderClient = await CreateClientForUserAsync(factory, "intruder", "Intruder", "Project Officer");
        var delete = await intruderClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{projectId}/remarks/{created!.Id}")
        {
            Content = JsonContent.Create(new
            {
                rowVersion = created.RowVersion,
                actorRole = RemarkActorRole.ProjectOfficer.ToString()
            }, options: SerializerOptions)
        });

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        var problem = await delete.Content.ReadFromJsonAsync<ProblemDetailsDto>(SerializerOptions);
        Assert.NotNull(problem);
        Assert.Equal(RemarkService.PermissionDeniedMessage, problem!.Title);
    }

    [Fact]
    public async Task ListRemarksAsync_FiltersByCanonicalRoleValues()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 604;

        var poClient = await CreateClientForUserAsync(factory, "user-po", "Project Officer", "Project Officer");
        var adminClient = await CreateClientForUserAsync(factory, "user-admin", "Admin", "Admin");

        await SeedProjectAsync(factory, projectId, leadPoUserId: "user-po");

        var createPo = await poClient.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "PO remark",
            eventDate = new DateOnly(2024, 9, 1),
            stageRef = StageCodes.FS
        });

        Assert.Equal(HttpStatusCode.Created, createPo.StatusCode);
        var poRemark = await createPo.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(poRemark);

        var createAdmin = await adminClient.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "Admin remark",
            eventDate = new DateOnly(2024, 9, 2),
            stageRef = StageCodes.FS
        });

        Assert.Equal(HttpStatusCode.Created, createAdmin.StatusCode);
        var adminRemark = await createAdmin.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(adminRemark);

        var poFilterResponse = await adminClient.GetAsync($"/api/projects/{projectId}/remarks?role=ProjectOfficer");
        Assert.Equal(HttpStatusCode.OK, poFilterResponse.StatusCode);
        var poList = await poFilterResponse.Content.ReadFromJsonAsync<RemarkListResponseDto>(SerializerOptions);
        Assert.NotNull(poList);
        Assert.All(poList!.Items, item => Assert.Equal(RemarkActorRole.ProjectOfficer, item.AuthorRole));
        Assert.Contains(poList.Items, item => item.Id == poRemark!.Id);
        Assert.DoesNotContain(poList.Items, item => item.Id == adminRemark!.Id);

        var adminFilterResponse = await adminClient.GetAsync($"/api/projects/{projectId}/remarks?role=Admin");
        Assert.Equal(HttpStatusCode.OK, adminFilterResponse.StatusCode);
        var adminList = await adminFilterResponse.Content.ReadFromJsonAsync<RemarkListResponseDto>(SerializerOptions);
        Assert.NotNull(adminList);
        Assert.All(adminList!.Items, item => Assert.Equal(RemarkActorRole.Administrator, item.AuthorRole));
        Assert.Contains(adminList.Items, item => item.Id == adminRemark!.Id);
        Assert.DoesNotContain(adminList.Items, item => item.Id == poRemark!.Id);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<HttpClient> CreateClientForUserAsync(RemarkApiFactory factory, string userId, string fullName, params string[] roles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        }

        await SeedUserAsync(factory, userId, fullName, roles);
        return client;
    }

    private static async Task SeedUserAsync(RemarkApiFactory factory, string userId, string fullName, IReadOnlyCollection<string> roles)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(e => e.Description)));
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
                FullName = fullName
            };
            var createResult = await userManager.CreateAsync(user);
            Assert.True(createResult.Succeeded, string.Join(",", createResult.Errors.Select(e => e.Description)));
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (!await userManager.IsInRoleAsync(user, role))
            {
                var addResult = await userManager.AddToRoleAsync(user, role);
                Assert.True(addResult.Succeeded, string.Join(",", addResult.Errors.Select(e => e.Description)));
            }
        }
    }

    private static async Task SeedProjectAsync(RemarkApiFactory factory, int projectId, string leadPoUserId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (await db.Projects.AnyAsync(p => p.Id == projectId))
        {
            return;
        }

        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "seed",
            LeadPoUserId = leadPoUserId
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = projectId,
            StageCode = StageCodes.FS,
            SortOrder = 1,
            Status = StageStatus.NotStarted
        });

        await db.SaveChangesAsync();
    }

    private static async Task SetRemarkCreatedAtAsync(RemarkApiFactory factory, int remarkId, DateTime createdAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var remark = await db.Remarks.SingleAsync(r => r.Id == remarkId);
        remark.CreatedAtUtc = createdAtUtc;
        await db.SaveChangesAsync();
    }

    private static async Task<string> GetRemarkRowVersionAsync(RemarkApiFactory factory, int remarkId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var remark = await db.Remarks.AsNoTracking().SingleAsync(r => r.Id == remarkId);
        return Convert.ToBase64String(remark.RowVersion);
    }

    private sealed record RemarkResponseDto
    {
        public int Id { get; init; }
        public int ProjectId { get; init; }
        public RemarkType Type { get; init; }
        public RemarkActorRole AuthorRole { get; init; }
        public string AuthorUserId { get; init; } = string.Empty;
        public string AuthorDisplayName { get; init; } = string.Empty;
        public string AuthorInitials { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public DateOnly EventDate { get; init; }
        public string? StageRef { get; init; }
        public string? StageName { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? LastEditedAtUtc { get; init; }
        public bool IsDeleted { get; init; }
        public DateTime? DeletedAtUtc { get; init; }
        public string? DeletedByUserId { get; init; }
        public RemarkActorRole? DeletedByRole { get; init; }
        public string? DeletedByDisplayName { get; init; }
        public string RowVersion { get; init; } = string.Empty;
    }

    private sealed record RemarkListResponseDto
    {
        public int Total { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IReadOnlyList<RemarkResponseDto> Items { get; init; } = Array.Empty<RemarkResponseDto>();
    }

    private sealed record ProblemDetailsDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public int? Status { get; init; }
    }

    private sealed class RemarkApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"remarks-{Guid.NewGuid()}"));
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

            var rolesHeader = Request.Headers["X-Test-Roles"].ToString();
            var roles = string.IsNullOrWhiteSpace(rolesHeader)
                ? Array.Empty<string>()
                : rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId)
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
