using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Hosted;

namespace ProjectManagement.Tests.Infrastructure;

public sealed class PrismTestOcrStartupTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Test_host_starts_without_the_deployment_ocr_executable(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ProjectDocuments:Ocr:OcrExecutablePath"] = "C:/Python311/Scripts/ocrmypdf.exe",
                    ["DocRepo:OcrExecutablePath"] = "C:/Python311/Scripts/ocrmypdf.exe",
                    ["ProjectDocuments:Ocr:EnableWorker"] = "true",
                    ["DocRepo:EnableOcrWorker"] = "true",
                    ["OcrBackfill:Enabled"] = "true"
                }));
            builder.UsePrismTestInfrastructure($"ocr-startup-{environment}");
        });

        // CreateClient starts the real host, including ValidateOnStart, on Linux CI.
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<ProjectDocumentOcrOptions>>().Value;
        Assert.False(options.EnableWorker);
        Assert.True(string.IsNullOrWhiteSpace(options.OcrExecutablePath));
        Assert.True(Path.IsPathFullyQualified(options.WorkRoot));

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var repository = Assert.IsType<FileSystemXmlRepository>(factory.Services
            .GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        Assert.Equal(Path.GetFullPath(configuration["DP_KEYS_DIR"]!), repository.Directory.FullName);
        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PRISM-ERP-Tests")) + Path.DirectorySeparatorChar,
            repository.Directory.FullName,
            StringComparison.Ordinal);
        Assert.True(repository.Directory.Exists);

        // Exercise the real persistent key repository, not just the configured path.
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PrismTestOcrStartupTests");
        var protectedValue = protector.Protect("test-host-key-persistence");
        Assert.Equal("test-host-key-persistence", protector.Unprotect(protectedValue));
        Assert.NotEmpty(repository.Directory.GetFiles("key-*.xml"));

        if (environment == "Production")
        {
            var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);
            Assert.Equal("__Host-PMAuth", cookie.Cookie.Name);
            Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        }

        Assert.False(configuration.GetValue<bool>("DocRepo:EnableOcrWorker"));
        Assert.True(string.IsNullOrWhiteSpace(configuration["DocRepo:OcrExecutablePath"]));
        Assert.False(configuration.GetValue<bool>("OcrBackfill:Enabled"));
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), worker =>
            worker is ProjectDocumentOcrWorker or DocRepoOcrWorker or OcrTextBackfillWorker);
    }

    [Fact]
    public void Production_validator_still_rejects_a_missing_configured_executable()
    {
        var missingExecutable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ocrmypdf.exe");
        var result = new ProjectDocumentOcrOptionsValidator().Validate(null, new ProjectDocumentOcrOptions
        {
            WorkRoot = Path.GetTempPath(),
            EnableWorker = false,
            OcrExecutablePath = missingExecutable
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("OcrExecutablePath", StringComparison.Ordinal)
            && failure.Contains("does not exist", StringComparison.Ordinal));
    }
}
