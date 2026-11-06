using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// "Run checks again" was a link to ?Refresh=true, so any link, bookmark or repeated reload
/// re-ran every check (probe files written to disk, up to 10 s each) and bypassed the cache.
/// It is now a POST with the antiforgery token; viewing the page uses the cached checks.
/// </summary>
public sealed class SystemHealthRefreshEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string PagePath = "/Admin/Diagnostics/DbHealth";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly RecordingHealth _health = new();

    public SystemHealthRefreshEndToEndTests(WebApplicationFactory<Program> factory)
    {
        var databaseName = "system-health-refresh-" + Guid.NewGuid().ToString("N");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UsePrismTestInfrastructure("system-health-refresh");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll(typeof(DbContextOptions<MediaLibraryDbContext>));
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
                services.AddDbContext<MediaLibraryDbContext>(o => o.UseInMemoryDatabase(databaseName + "-media"));
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAdminSystemHealthService>();
                services.AddSingleton<IAdminSystemHealthService>(_health);
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

    [Fact]
    public async Task Viewing_NeverForcesARefresh_AndRunChecksAgainIsAPostForm()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await client.GetAsync(PagePath + "?Refresh=true");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(new[] { false }, _health.Calls);
        var form = Regex.Match(html, @"<form method=""post"" action=""[^""]*DbHealth\?handler=Refresh""[^>]*>(?<body>.*?)</form>", RegexOptions.Singleline);
        Assert.True(form.Success, "The Run checks again form is missing.");
        Assert.Contains("__RequestVerificationToken", form.Groups["body"].Value);
        Assert.Contains("Run checks again", form.Groups["body"].Value);
        Assert.DoesNotContain("Refresh=true", html);
    }

    [Fact]
    public async Task RunChecksAgain_PostRefreshes()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await client.GetStringAsync(PagePath);
        var token = Regex.Match(page, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token));

        var response = await client.PostAsync(
            PagePath + "?handler=Refresh",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        // The checks run once, then the page redirects to itself, so reloading it does not post
        // the form again and re-run every check.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.EndsWith(PagePath, response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { false, true }, _health.Calls);
    }

    private sealed class RecordingHealth : IAdminSystemHealthService
    {
        public List<bool> Calls { get; } = new();

        public Task<AdminSystemHealthSnapshot> CheckAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            lock (Calls)
            {
                Calls.Add(forceRefresh);
            }

            return Task.FromResult(new AdminSystemHealthSnapshot(
                AdminHealthStatus.Healthy,
                DateTimeOffset.UtcNow,
                "Test",
                "test",
                Array.Empty<AdminSystemHealthCheck>(),
                new DatabaseHealthSnapshot(
                    true, null, null, null, null, null, null, "(test)",
                    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                    Array.Empty<DatabaseHealthCheck>())));
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
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "admin-1"),
                    new Claim(ClaimTypes.Name, "admin-1"),
                    new Claim(ClaimTypes.Role, "Admin")
                },
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
