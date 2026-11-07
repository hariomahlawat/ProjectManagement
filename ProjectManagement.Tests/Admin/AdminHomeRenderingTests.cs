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
/// Home tells the truth (docs/ux/admin-review-2-fixes.md, PR 3). The finder's "nothing matches"
/// text showed Razor code, because "word@if" reads as an e-mail address; and straight after
/// start-up the status line said "Nothing needs you" before any system check had run.
/// </summary>
public sealed class AdminHomeRenderingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminHomeRenderingTests(WebApplicationFactory<Program> factory)
    {
        var databaseName = "admin-home-rendering-" + Guid.NewGuid().ToString("N");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UsePrismTestInfrastructure("admin-home-rendering");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll(typeof(DbContextOptions<MediaLibraryDbContext>));
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
                services.AddDbContext<MediaLibraryDbContext>(o => o.UseInMemoryDatabase(databaseName + "-media"));
            });
            builder.ConfigureTestServices(services =>
            {
                // No check has run since start-up: GetLatest is null.
                services.RemoveAll<IAdminSystemHealthService>();
                services.AddSingleton<IAdminSystemHealthService, NotRunHealth>();
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
    public async Task Home_FinderEmptyText_IsPlainWords_AndTheStatusNeverSaysAllClearBeforeTheChecksRun()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var empty = Regex.Match(html, @"<p class=""admin-home-find__empty""[^>]*>(?<body>.*?)</p>", RegexOptions.Singleline);
        Assert.True(empty.Success, "The finder's empty text is missing.");
        var text = Regex.Replace(Regex.Replace(empty.Groups["body"].Value, "<[^>]+>", string.Empty), @"\s+", " ").Trim();
        Assert.Equal("Nothing matches. Try another word, or open Every admin page.", text);
        Assert.DoesNotContain("@", empty.Groups["body"].Value);

        var status = Regex.Match(html, @"<section class=""admin-home-status[^""]*""[^>]*>(?<body>.*?)</section>", RegexOptions.Singleline);
        Assert.True(status.Success, "The status line is missing.");
        Assert.Contains("Not checked yet", status.Groups["body"].Value);
        Assert.Contains("Open System health to run them.", status.Groups["body"].Value);
        Assert.DoesNotContain("Nothing needs you", status.Groups["body"].Value);
    }

    // The same leak anywhere in Administration: a word written straight before @if, @foreach and
    // the like is read as an e-mail address, so the code is shown on screen.
    [Fact]
    public void AdminViews_NeverWriteAWordStraightBeforeACodeBlock()
    {
        var root = FindRepositoryRoot();
        var leaks = Directory.EnumerateFiles(Path.Combine(root, "Areas", "Admin"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(file =>
            {
                var source = Regex.Replace(File.ReadAllText(file), @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
                return Regex.Matches(source, @"(?<![@.\w])[A-Za-z0-9]+@(if|foreach|for|while|switch|using)\b")
                    .Select(match => $"{Path.GetRelativePath(root, file)}: {match.Value}");
            })
            .ToArray();

        Assert.Empty(leaks);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class NotRunHealth : IAdminSystemHealthService
    {
        public Task<AdminSystemHealthSnapshot> CheckAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Home must not run the system checks.");

        public AdminSystemHealthSnapshot? GetLatest() => null;
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
