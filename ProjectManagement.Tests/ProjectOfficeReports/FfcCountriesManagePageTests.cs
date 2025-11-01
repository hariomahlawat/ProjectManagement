using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Countries;
using ProjectManagement.Data;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcCountriesManagePageTests
{
    [Fact]
    public async Task OnGetAsync_WithQueryAndSorting_LoadsExpectedCountries()
    {
        await using var db = CreateDbContext();
        db.FfcCountries.AddRange(
            new FfcCountry { Name = "India", IsoCode = "IND", IsActive = true },
            new FfcCountry { Name = "Japan", IsoCode = "JPN", IsActive = true },
            new FfcCountry { Name = "Indonesia", IsoCode = "IDN", IsActive = false });
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        page.Query = "ind";
        page.Sort = "iso";
        page.SortDirection = "desc";
        ConfigurePageContext(page, CreateAdminPrincipal());

        var result = await page.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(2, page.Countries.Count);
        Assert.Equal(new[] { "India", "Indonesia" }, page.Countries.Select(c => c.Name));
        Assert.Equal("iso", page.CurrentSort);
        Assert.Equal("desc", page.CurrentSortDirection);
    }

    [Fact]
    public async Task OnPostToggleActiveAsync_TogglesStatus()
    {
        await using var db = CreateDbContext();
        var country = new FfcCountry { Name = "Canada", IsoCode = "CAN", IsActive = true };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        var result = await page.OnPostToggleActiveAsync(country.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);

        var updated = await db.FfcCountries.AsNoTracking().SingleAsync();
        Assert.False(updated.IsActive);
        Assert.Equal("Country deactivated.", page.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task OnPostToggleActiveAsync_WithoutPermission_ReturnsForbid()
    {
        await using var db = CreateDbContext();
        var country = new FfcCountry { Name = "Chile", IsoCode = "CHL", IsActive = true };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        ConfigurePageContext(page, CreateGeneralPrincipal());

        var result = await page.OnPostToggleActiveAsync(country.Id);

        Assert.IsType<ForbidResult>(result);
        var unchanged = await db.FfcCountries.AsNoTracking().SingleAsync();
        Assert.True(unchanged.IsActive);
    }

    [Fact]
    public async Task UnauthorizedUser_IsRedirectedToAccessDenied()
    {
        using var factory = new FfcCountriesManagePageFactory();
        var client = CreateClientForUser(factory, "general-user");

        var response = await client.GetAsync("/ProjectOfficeReports/FFC/Countries/Manage");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/Identity/Account/AccessDenied?ReturnUrl=%2FProjectOfficeReports%2FFFC%2FCountries%2FManage", location.PathAndQuery);
    }

    [Fact]
    public async Task UnauthorizedUser_SeesFriendlyMessageOnAccessDenied()
    {
        using var factory = new FfcCountriesManagePageFactory();
        var client = CreateClientForUser(factory, "general-user");

        var response = await client.GetAsync("/ProjectOfficeReports/FFC/Countries/Manage");
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var accessDenied = await client.GetAsync(new Uri(client.BaseAddress!, location));

        Assert.Equal(HttpStatusCode.Forbidden, accessDenied.StatusCode);
        var content = await accessDenied.Content.ReadAsStringAsync();
        Assert.Contains("Only administrators or heads of department can manage FFC records or countries", content, StringComparison.OrdinalIgnoreCase);
    }

    private static ManageModel CreatePage(ApplicationDbContext db)
        => new(db, new StubAuditService(), NullLogger<ManageModel>.Instance);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal CreateAdminPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin"),
            new(ClaimTypes.Name, "admin"),
            new(ClaimTypes.Role, "Admin")
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal CreateGeneralPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user"),
            new(ClaimTypes.Name, "user")
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private static HttpClient CreateClientForUser(FfcCountriesManagePageFactory factory, string userId, params string[] roles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        }

        return client;
    }

    private sealed class FfcCountriesManagePageFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase($"ffc-countries-manage-{Guid.NewGuid()}"));
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
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
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
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, userId)
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

    private sealed class StubAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
