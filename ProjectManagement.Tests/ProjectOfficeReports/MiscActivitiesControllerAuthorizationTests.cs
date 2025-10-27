using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class MiscActivitiesControllerAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string? _previousUploadRoot;
    private readonly string _uploadRoot;

    public MiscActivitiesControllerAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _previousUploadRoot = Environment.GetEnvironmentVariable("PM_UPLOAD_ROOT");
        _uploadRoot = Path.Combine(Path.GetTempPath(), "pm-misc-activities-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", _uploadRoot);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("misc-activities-tests"));
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
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", _previousUploadRoot);
        try
        {
            if (Directory.Exists(_uploadRoot))
            {
                Directory.Delete(_uploadRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task List_AllowsViewerRole()
    {
        await ResetDatabaseAsync();
        await SeedActivityAsync("viewer-seed");

        var client = CreateClient(role: "Main Office", userId: "viewer-1");
        var response = await client.GetAsync("/api/project-office-reports/misc-activities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_ForbidsUnauthorizedRole()
    {
        await ResetDatabaseAsync();

        var client = CreateClient(role: "Guest", userId: "unauthorized");
        var response = await client.GetAsync("/api/project-office-reports/misc-activities");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AllowsManagerRole()
    {
        await ResetDatabaseAsync();

        var client = CreateClient(role: "TA", userId: "ta-user");
        var payload = new
        {
            activityTypeId = (Guid?)null,
            occurrenceDate = "2024-01-15",
            nomenclature = "Authorized activity",
            description = "Created via API",
            externalLink = (string?)null
        };

        var response = await client.PostAsJsonAsync("/api/project-office-reports/misc-activities", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activity = await db.MiscActivities.SingleAsync();
        Assert.Equal("ta-user", activity.CapturedByUserId);
        Assert.Equal("Authorized activity", activity.Nomenclature);
    }

    [Fact]
    public async Task Create_ForbidsReadOnlyRole()
    {
        await ResetDatabaseAsync();

        var client = CreateClient(role: "Main Office", userId: "viewer-2");
        var payload = new
        {
            activityTypeId = (Guid?)null,
            occurrenceDate = "2024-01-15",
            nomenclature = "Should fail",
            description = "Read-only user",
            externalLink = (string?)null
        };

        var response = await client.PostAsJsonAsync("/api/project-office-reports/misc-activities", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_AllowsManagerRole()
    {
        await ResetDatabaseAsync();
        var activity = await SeedActivityAsync("update-seed");

        var client = CreateClient(role: "TA", userId: "editor-1");
        var payload = new
        {
            activityTypeId = (Guid?)null,
            occurrenceDate = "2024-02-01",
            nomenclature = "Updated name",
            description = "Updated via API",
            externalLink = (string?)null,
            rowVersion = Convert.ToBase64String(activity.RowVersion)
        };

        var response = await client.PutAsJsonAsync($"/api/project-office-reports/misc-activities/{activity.Id}", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshed = await db.MiscActivities.SingleAsync();
        Assert.Equal("editor-1", refreshed.LastModifiedByUserId);
        Assert.Equal("Updated name", refreshed.Nomenclature);
    }

    [Fact]
    public async Task Update_ForbidsReadOnlyRole()
    {
        await ResetDatabaseAsync();
        var activity = await SeedActivityAsync("update-forbidden");

        var client = CreateClient(role: "Main Office", userId: "viewer-3");
        var payload = new
        {
            activityTypeId = (Guid?)null,
            occurrenceDate = "2024-02-01",
            nomenclature = "Should not update",
            description = "Attempted by read-only user",
            externalLink = (string?)null,
            rowVersion = Convert.ToBase64String(activity.RowVersion)
        };

        var response = await client.PutAsJsonAsync($"/api/project-office-reports/misc-activities/{activity.Id}", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AllowsApproverRole()
    {
        await ResetDatabaseAsync();
        var activity = await SeedActivityAsync("delete-seed");

        var client = CreateClient(role: "HoD", userId: "approver-1");
        var rowVersion = Convert.ToBase64String(activity.RowVersion);
        var response = await client.DeleteAsync($"/api/project-office-reports/misc-activities/{activity.Id}?rowVersion={Uri.EscapeDataString(rowVersion)}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshed = await db.MiscActivities.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(refreshed.DeletedUtc);
        Assert.Equal("approver-1", refreshed.DeletedByUserId);
    }

    [Fact]
    public async Task Delete_ForbidsManagerRole()
    {
        await ResetDatabaseAsync();
        var activity = await SeedActivityAsync("delete-forbidden");

        var client = CreateClient(role: "TA", userId: "editor-2");
        var rowVersion = Convert.ToBase64String(activity.RowVersion);
        var response = await client.DeleteAsync($"/api/project-office-reports/misc-activities/{activity.Id}?rowVersion={Uri.EscapeDataString(rowVersion)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadMedia_AllowsManagerRole()
    {
        await ResetDatabaseAsync();
        var activity = await SeedActivityAsync("media-upload");

        var client = CreateClient(role: "TA", userId: "uploader-1");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "evidence.pdf");
        content.Add(new StringContent(Convert.ToBase64String(activity.RowVersion), Encoding.UTF8), "rowVersion");
        content.Add(new StringContent("Signed attendance", Encoding.UTF8), "caption");

        var response = await client.PostAsync($"/api/project-office-reports/misc-activities/{activity.Id}/media", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshed = await db.MiscActivities.Include(x => x.Media).SingleAsync();
        var media = Assert.Single(refreshed.Media);
        Assert.Equal("uploader-1", media.UploadedByUserId);
        Assert.Equal("application/pdf", media.MediaType);
    }

    [Fact]
    public async Task UploadMedia_ForbidsReadOnlyRole()
    {
        await ResetDatabaseAsync();
        var activity = await SeedActivityAsync("media-forbidden");

        var client = CreateClient(role: "Main Office", userId: "viewer-4");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "evidence.pdf");
        content.Add(new StringContent(Convert.ToBase64String(activity.RowVersion), Encoding.UTF8), "rowVersion");

        var response = await client.PostAsync($"/api/project-office-reports/misc-activities/{activity.Id}/media", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string? role = null, string? userId = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        if (!string.IsNullOrEmpty(role))
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        }

        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<MiscActivity> SeedActivityAsync(string nomenclature)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var activity = new MiscActivity
        {
            Id = Guid.NewGuid(),
            Nomenclature = nomenclature,
            OccurrenceDate = new DateOnly(2024, 1, 1),
            Description = "Seeded activity",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            CapturedByUserId = "creator",
            LastModifiedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedByUserId = "creator",
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        db.MiscActivities.Add(activity);
        await db.SaveChangesAsync();

        return activity;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "test-user") };
            var role = Request.Headers["X-Test-Role"].ToString();
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var userId = Request.Headers["X-Test-UserId"].ToString();
            if (!string.IsNullOrEmpty(userId))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
