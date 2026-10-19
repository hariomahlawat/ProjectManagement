using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
                    ["Security:LoginRateLimit:WindowMinutes"] = "5"
                }));
        });
    }

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
        var partition = LoginRateLimiting.GetPartition(Request("GET", "10.0.0.5"), new LoginRateLimitOptions { PermitLimit = 1 });
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

        var first = LoginRateLimiting.GetPartition(Request("POST", "10.0.0.5"), options);
        var sameClient = LoginRateLimiting.GetPartition(Request("POST", "::ffff:10.0.0.5"), options);
        var otherClient = LoginRateLimiting.GetPartition(Request("POST", "10.0.0.6"), options);

        Assert.Equal(first.PartitionKey, sameClient.PartitionKey);
        Assert.NotEqual(first.PartitionKey, otherClient.PartitionKey);
    }

    [Fact]
    public void Out_of_range_settings_are_clamped_instead_of_blocking_sign_in()
    {
        var options = new LoginRateLimitOptions { PermitLimit = 0, WindowMinutes = -3 };

        Assert.Equal(1, options.EffectivePermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), options.EffectiveWindow);
    }

    [Fact]
    public async Task Throttled_sign_in_returns_to_the_login_page_with_an_explanation()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserName"] = "someone",
            ["Input.Password"] = "wrong-password"
        });

        for (var i = 0; i < 2; i++)
        {
            var allowed = await client.PostAsync("/Identity/Account/Login?returnUrl=%2FProjects", form);
            Assert.NotEqual(HttpStatusCode.Redirect, allowed.StatusCode);
        }

        var throttled = await client.PostAsync("/Identity/Account/Login?returnUrl=%2FProjects", form);

        Assert.Equal(HttpStatusCode.Redirect, throttled.StatusCode);
        Assert.Equal("/Identity/Account/Login?throttled=true&returnUrl=%2FProjects", throttled.Headers.Location?.OriginalString);

        // The login page itself still opens for this client and for everyone else.
        for (var i = 0; i < 5; i++)
        {
            var page = await client.GetAsync(throttled.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Too many sign-in attempts from this computer", await page.Content.ReadAsStringAsync());
        }
    }
}
