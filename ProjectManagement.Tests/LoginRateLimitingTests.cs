using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProjectManagement.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class LoginRateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LoginRateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UsePrismTestInfrastructure("login-rate-limit");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:LoginRateLimit:PermitLimit"] = "2",
                    ["Security:LoginRateLimit:ServerPermitLimit"] = "3",
                    ["Security:LoginRateLimit:WindowMinutes"] = "60" // long, so a slow run cannot reset it mid-test
                }));
            builder.ConfigureTestServices(services =>
                services.AddTransient<IStartupFilter, PeerAddressStartupFilter>());
        });
    }

    private static readonly SharedRateLimiters Shared = new(
        Options.Create(new LoginRateLimitOptions()), Options.Create(new DocumentUploadRateLimitOptions()));

    private static HttpContext Request(string method, string ip)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = LoginRateLimiting.LoginPath;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public void Opening_the_login_page_is_never_throttled()
    {
        var partition = LoginRateLimiting.GetPartition(Request("GET", "10.0.0.5"), new LoginRateLimitOptions { PermitLimit = 1 }, Shared.Login);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        }
    }

    [Fact]
    public void Sign_in_attempts_are_partitioned_per_client_address()
    {
        var options = new LoginRateLimitOptions();

        var first = LoginRateLimiting.GetPartition(Request("POST", "10.0.0.5"), options, Shared.Login);
        var sameClient = LoginRateLimiting.GetPartition(Request("POST", "::ffff:10.0.0.5"), options, Shared.Login);
        var otherClient = LoginRateLimiting.GetPartition(Request("POST", "10.0.0.6"), options, Shared.Login);

        Assert.Equal(first.PartitionKey, sameClient.PartitionKey);
        Assert.NotEqual(first.PartitionKey, otherClient.PartitionKey);
    }

    [Fact]
    public void Out_of_range_settings_are_clamped_instead_of_blocking_sign_in()
    {
        var low = new LoginRateLimitOptions { PermitLimit = 0, ServerPermitLimit = 0, WindowMinutes = -3 };
        var high = new LoginRateLimitOptions { PermitLimit = 50, ServerPermitLimit = 10, WindowMinutes = 10_000 };

        Assert.Equal(1, low.EffectivePermitLimit);
        Assert.Equal(1, low.EffectiveServerPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), low.EffectiveWindow);
        Assert.Equal(50, high.EffectiveServerPermitLimit); // never below the per-client limit
        Assert.Equal(TimeSpan.FromMinutes(60), high.EffectiveWindow);
    }

    private async Task<HttpResponseMessage> SignInAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login?returnUrl=%2FProjects")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.UserName"] = "someone",
                ["Input.Password"] = "wrong-password"
            })
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Sign_in_is_limited_per_client_and_capped_across_forged_clients()
    {
        // PermitLimit 2 per client, ServerPermitLimit 3 in total (see constructor).
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string throttledLocation = "/Identity/Account/Login?throttled=true&returnUrl=%2FProjects";

        // Requests that pass the limiter reach the page and fail antiforgery validation (400).
        for (var i = 1; i <= 2; i++)
        {
            var allowed = await SignInAsync(client, "10.0.0.1");
            Assert.True(allowed.StatusCode == HttpStatusCode.BadRequest, $"attempt {i}: {allowed.StatusCode} {allowed.Headers.Location}");
        }

        // Client A's third attempt is refused by its own allowance and does not touch the shared one.
        var clientAThird = await SignInAsync(client, "10.0.0.1");
        Assert.Equal(HttpStatusCode.Redirect, clientAThird.StatusCode);
        Assert.Equal(throttledLocation, clientAThird.Headers.Location?.OriginalString);

        // Another client still has its own allowance...
        var clientB = await SignInAsync(client, "10.0.0.2");
        Assert.True(clientB.StatusCode == HttpStatusCode.BadRequest, $"client B: {clientB.StatusCode} {clientB.Headers.Location}");

        // ...but with 3 attempts admitted in total, a fresh (forged) address hits the server-wide cap.
        var forged = await SignInAsync(client, "10.9.9.9");
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Equal(throttledLocation, forged.Headers.Location?.OriginalString);

        // Opening the login page is never throttled and explains the wait.
        for (var i = 0; i < 5; i++)
        {
            var page = await client.GetAsync(throttledLocation);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Too many sign-in attempts. Wait up to 60 minutes", await page.Content.ReadAsStringAsync());
        }
    }

    // TestServer leaves RemoteIpAddress null, and ForwardedHeaders ignores X-Forwarded-For
    // without a peer address. Give every request the loopback peer IIS would present.
    private sealed class PeerAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
