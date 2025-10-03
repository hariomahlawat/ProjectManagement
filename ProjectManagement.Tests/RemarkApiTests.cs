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
    private static readonly IReadOnlyDictionary<RemarkActorRole, IReadOnlyList<string>> FriendlyRoleNames = new Dictionary<RemarkActorRole, IReadOnlyList<string>>
    {
        [RemarkActorRole.ProjectOfficer] = new[] { "Project Officer" },
        [RemarkActorRole.HeadOfDepartment] = new[] { "HoD", "Head of Department" },
        [RemarkActorRole.Commandant] = new[] { "Comdt" },
        [RemarkActorRole.Administrator] = new[] { "Admin" },
        [RemarkActorRole.Mco] = new[] { "MCO" },
        [RemarkActorRole.ProjectOffice] = new[] { "Project Office" },
        [RemarkActorRole.MainOffice] = new[] { "Main Office" },
        [RemarkActorRole.Ta] = new[] { "TA" }
    };

    public static IEnumerable<object[]> CanonicalActorRoles
    {
        get
        {
            foreach (var role in Enum.GetValues<RemarkActorRole>())
            {
                if (role == RemarkActorRole.Unknown)
                {
                    continue;
                }

                yield return new object[] { role.ToString() };
            }
        }
    }

    public static IEnumerable<object[]> FriendlyRoleFilters
    {
        get
        {
            foreach (var pair in FriendlyRoleNames)
            {
                foreach (var alias in pair.Value)
                {
                    yield return new object[] { pair.Key, alias };
                }
            }
        }
    }

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
    public async Task ProjectOfficerAssignedWithoutIdentityRole_CanCreateAndListRemarks()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 9600;
        var client = await CreateClientForUserAsync(factory, "po-fallback", "PO Fallback", false, "Project Officer");
        await SeedProjectAsync(factory, projectId, leadPoUserId: "po-fallback");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "Fallback PO remark",
            eventDate = today,
            stageRef = StageCodes.FS
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(created);
        Assert.Equal("po-fallback", created!.AuthorUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, created.AuthorRole);

        var list = await client.GetFromJsonAsync<RemarkListResponseDto>($"/api/projects/{projectId}/remarks", SerializerOptions);
        Assert.NotNull(list);
        Assert.Equal(1, list!.Total);
        Assert.Single(list.Items);
        Assert.Equal("po-fallback", list.Items[0].AuthorUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, list.Items[0].AuthorRole);
    }

    [Fact]
    public async Task HeadOfDepartmentAssignedWithoutIdentityRole_CanCreateAndListRemarks()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 9601;
        var client = await CreateClientForUserAsync(factory, "hod-fallback", "HoD Fallback", false, "HoD");
        await SeedProjectAsync(factory, projectId, leadPoUserId: "po-owner", hodUserId: "hod-fallback");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = "Fallback HoD remark",
            eventDate = today,
            stageRef = StageCodes.FS,
            actorRole = RemarkActorRole.HeadOfDepartment.ToString()
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(created);
        Assert.Equal("hod-fallback", created!.AuthorUserId);
        Assert.Equal(RemarkActorRole.HeadOfDepartment, created.AuthorRole);

        var list = await client.GetFromJsonAsync<RemarkListResponseDto>(
            $"/api/projects/{projectId}/remarks?actorRole={Uri.EscapeDataString(RemarkActorRole.HeadOfDepartment.ToString())}",
            SerializerOptions);
        Assert.NotNull(list);
        Assert.Equal(1, list!.Total);
        Assert.Single(list.Items);
        Assert.Equal(RemarkActorRole.HeadOfDepartment, list.Items[0].AuthorRole);
        Assert.Equal("hod-fallback", list.Items[0].AuthorUserId);
    }

    [Fact]
    public async Task ListRemarksAsync_ViewerWithoutRemarkRole_ReturnsForbidden()
    {
        using var factory = new RemarkApiFactory();
        var projectId = 9610;
        await SeedProjectAsync(factory, projectId, leadPoUserId: "lead-owner");

        var viewerClient = await CreateClientForUserAsync(
            factory,
            "viewer-no-role",
            "Viewer No Role",
            false,
            "Project Officer");

        var response = await viewerClient.GetAsync($"/api/projects/{projectId}/remarks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(SerializerOptions);
        Assert.NotNull(problem);
        Assert.Equal(RemarkService.PermissionDeniedMessage, problem!.Title);
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

    [Theory]
    [MemberData(nameof(CanonicalActorRoles))]
    public async Task RemarkLifecycle_Succeeds_WithCanonicalActorRoleIdentifiers(string canonicalRole)
    {
        using var factory = new RemarkApiFactory();
        var roleEnum = Enum.Parse<RemarkActorRole>(canonicalRole);
        var projectId = 700 + (int)roleEnum;
        var userId = $"user-{canonicalRole.ToLowerInvariant()}";
        var client = await CreateClientForUserAsync(factory, userId, $"User {canonicalRole}", canonicalRole);
        await SeedProjectAsync(factory, projectId, leadPoUserId: userId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var remarkBody = $"Initial remark for {canonicalRole}";

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = remarkBody,
            eventDate = today,
            stageRef = StageCodes.FS,
            actorRole = canonicalRole
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(created);
        Assert.Equal(userId, created!.AuthorUserId);
        Assert.Equal(remarkBody, created.Body);

        var list = await client.GetFromJsonAsync<RemarkListResponseDto>($"/api/projects/{projectId}/remarks?actorRole={Uri.EscapeDataString(canonicalRole)}", SerializerOptions);
        Assert.NotNull(list);
        Assert.Equal(1, list!.Total);
        Assert.Single(list.Items);
        Assert.Equal(created.Id, list.Items[0].Id);

        var updateResponse = await client.PutAsJsonAsync($"/api/projects/{projectId}/remarks/{created.Id}", new
        {
            body = $"Updated remark for {canonicalRole}",
            eventDate = today,
            stageRef = StageCodes.FS,
            rowVersion = created.RowVersion,
            actorRole = canonicalRole
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RemarkResponseDto>(SerializerOptions);
        Assert.NotNull(updated);
        Assert.Equal($"Updated remark for {canonicalRole}", updated!.Body);

        var deleteRequest = new
        {
            rowVersion = updated.RowVersion,
            actorRole = canonicalRole
        };

        var deleteResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{projectId}/remarks/{created.Id}")
        {
            Content = JsonContent.Create(deleteRequest, options: SerializerOptions)
        });

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deleted = await deleteResponse.Content.ReadFromJsonAsync<DeleteRemarkResponseDto>(SerializerOptions);
        Assert.NotNull(deleted);
        Assert.True(deleted!.Success);
        Assert.False(string.IsNullOrWhiteSpace(deleted.RowVersion));
    }

    [Theory]
    [MemberData(nameof(FriendlyRoleFilters))]
    public async Task ListRemarks_FilterByFriendlyRole_Succeeds(RemarkActorRole expectedRole, string friendlyRole)
    {
        using var factory = new RemarkApiFactory();
        var projectId = 900 + (int)expectedRole;
        var userId = $"friendly-{expectedRole.ToString().ToLowerInvariant()}";
        var otherRole = string.Equals(friendlyRole, "Admin", StringComparison.OrdinalIgnoreCase) ? "Project Officer" : "Admin";
        var client = await CreateClientForUserAsync(factory, userId, $"User {friendlyRole}", friendlyRole, otherRole);
        await SeedProjectAsync(factory, projectId, leadPoUserId: userId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var targetBody = $"Remark from {friendlyRole}";
        var otherBody = $"Remark from {otherRole}";

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = targetBody,
            eventDate = today,
            stageRef = StageCodes.FS,
            actorRole = friendlyRole
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var otherResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/remarks", new
        {
            type = RemarkType.Internal,
            body = otherBody,
            eventDate = today,
            stageRef = StageCodes.FS,
            actorRole = otherRole
        });

        Assert.Equal(HttpStatusCode.Created, otherResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/projects/{projectId}/remarks?role={Uri.EscapeDataString(friendlyRole)}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<RemarkListResponseDto>(SerializerOptions);
        Assert.NotNull(list);
        Assert.Equal(1, list!.Total);
        Assert.Single(list.Items);
        Assert.Equal(targetBody, list.Items[0].Body);
        Assert.Equal(expectedRole, list.Items[0].AuthorRole);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static Task<HttpClient> CreateClientForUserAsync(RemarkApiFactory factory, string userId, string fullName, params string[] roles)
        => CreateClientForUserAsync(factory, userId, fullName, true, roles);

    private static async Task<HttpClient> CreateClientForUserAsync(
        RemarkApiFactory factory,
        string userId,
        string fullName,
        bool persistIdentityRoles,
        params string[] roles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        }

        await SeedUserAsync(factory, userId, fullName, persistIdentityRoles, roles);
        return client;
    }

    private static async Task SeedUserAsync(
        RemarkApiFactory factory,
        string userId,
        string fullName,
        bool persistIdentityRoles,
        IReadOnlyCollection<string> roles)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (persistIdentityRoles)
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in roles.Distinct(StringComparer.Ordinal))
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(role));
                    Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(e => e.Description)));
                }
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

        if (persistIdentityRoles)
        {
            foreach (var role in roles.Distinct(StringComparer.Ordinal))
            {
                if (!await userManager.IsInRoleAsync(user, role))
                {
                    var addResult = await userManager.AddToRoleAsync(user, role);
                    Assert.True(addResult.Succeeded, string.Join(",", addResult.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    private static async Task SeedProjectAsync(
        RemarkApiFactory factory,
        int projectId,
        string leadPoUserId,
        string? hodUserId = null)
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
            LeadPoUserId = leadPoUserId,
            HodUserId = hodUserId
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
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? LastEditedAtUtc { get; init; }
        public bool IsDeleted { get; init; }
        public DateTimeOffset? DeletedAtUtc { get; init; }
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

    private sealed record DeleteRemarkResponseDto
    {
        public bool Success { get; init; }
        public string? RowVersion { get; init; }
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
