using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using Xunit;

namespace ProjectManagement.Tests;

// Runs uploads through the real pipeline, so it fails if UseRateLimiter moves back above
// UseAuthentication (per-user partitions would collapse) or the page loses its policy.
public sealed class DocumentUploadRateLimitingEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string UploadPath = "/DocumentRepository/Documents/Upload";
    private const string ThrottledLocation = UploadPath + "?throttled=true";

    private readonly WebApplicationFactory<Program> _factory;

    public DocumentUploadRateLimitingEndToEndTests(WebApplicationFactory<Program> factory)
    {
        var databaseName = "upload-rate-limit-" + Guid.NewGuid().ToString("N");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UsePrismTestInfrastructure("upload-rate-limit");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:DocumentUploadRateLimit:PermitLimit"] = "1",
                    ["Security:DocumentUploadRateLimit:ServerPermitLimit"] = "2",
                    ["Security:DocumentUploadRateLimit:WindowMinutes"] = "60" // long, so a slow run cannot reset it mid-test
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll(typeof(DbContextOptions<MediaLibraryDbContext>));
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
                services.AddDbContext<MediaLibraryDbContext>(o => o.UseInMemoryDatabase(databaseName + "-media"));
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

    private HttpClient ClientFor(string userId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        return client;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client)
        => client.PostAsync(UploadPath, new MultipartFormDataContent { { new StringContent("Throttle test"), "Input.Subject" } });

    [Fact]
    public async Task Uploads_are_limited_per_user_and_capped_server_wide()
    {
        // PermitLimit 1 per user, ServerPermitLimit 2 in total (see constructor).
        var clerkA = ClientFor("clerk-a");
        var clerkB = ClientFor("clerk-b");
        var clerkC = ClientFor("clerk-c");

        var first = await UploadAsync(clerkA);
        Assert.NotEqual(ThrottledLocation, first.Headers.Location?.OriginalString);

        var second = await UploadAsync(clerkA);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(ThrottledLocation, second.Headers.Location?.OriginalString);

        // Same client address, different user: own allowance.
        var otherUser = await UploadAsync(clerkB);
        Assert.NotEqual(ThrottledLocation, otherUser.Headers.Location?.OriginalString);

        // Two uploads admitted in total, so the server-wide cap (2) now applies to everyone.
        var capped = await UploadAsync(clerkC);
        Assert.Equal(ThrottledLocation, capped.Headers.Location?.OriginalString);

        // Opening the upload page is never throttled and explains the wait.
        var page = await clerkA.GetAsync(ThrottledLocation);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Too many uploads in a short time. Wait up to 60 minutes,", await page.Content.ReadAsStringAsync());
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-UserId"].ToString();
            if (string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, userId),
                    new Claim(ClaimTypes.Role, "Admin")
                },
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
