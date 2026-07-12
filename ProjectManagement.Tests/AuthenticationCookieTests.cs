using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AuthenticationCookieTests
{
    [Fact]
    public void Development_uses_http_safe_cookie_configuration()
    {
        // SECTION: Development must not issue a Secure __Host-prefixed cookie for HTTP fallback.
        using var factory = CreateFactory("Development");
        var options = GetApplicationCookieOptions(factory);

        Assert.Equal("PMAuth", options.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
        Assert.Equal("/", options.Cookie.Path);
    }

    [Fact]
    public void Production_uses_host_prefixed_secure_cookie_configuration()
    {
        // SECTION: Production keeps the hardened HTTPS-only cookie contract.
        using var factory = CreateFactory("Production");
        var options = GetApplicationCookieOptions(factory);

        Assert.Equal("__Host-PMAuth", options.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal("/", options.Cookie.Path);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UsePrismTestInfrastructure($"authentication-cookie-{environment}");
        });

    private static CookieAuthenticationOptions GetApplicationCookieOptions(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
    }
}
