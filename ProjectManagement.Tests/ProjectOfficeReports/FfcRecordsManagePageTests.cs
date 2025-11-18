using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;
using ProjectManagement.Data;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcRecordsManagePageTests
{
    [Theory]
    [InlineData(nameof(FfcRecordsManagePageTests.SetIpaMismatch), nameof(ManageModel.Input.IpaDate))]
    [InlineData(nameof(FfcRecordsManagePageTests.SetGslMismatch), nameof(ManageModel.Input.GslDate))]
    public async Task OnPostCreateAsync_WithFlagDateMismatch_AddsModelError(string setupMethod, string expectedKey)
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        page.Input = new ManageModel.InputModel
        {
            CountryId = country.Id,
            Year = 2024
        };

        typeof(FfcRecordsManagePageTests)
            .GetMethod(setupMethod, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?
            .Invoke(null, new object[] { page.Input });

        var result = await page.OnPostCreateAsync();

        Assert.IsType<PageResult>(result);
        var key = $"Input.{expectedKey}";
        Assert.True(page.ModelState.ContainsKey(key));
        Assert.Equal(0, await db.FfcRecords.CountAsync());
    }

    [Fact]
    public async Task OnPostCreateAsync_WithValidFlags_CreatesRecord()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        page.Input = new ManageModel.InputModel
        {
            CountryId = country.Id,
            Year = 2025,
            IpaYes = true,
            IpaDate = new DateOnly(2025, 1, 10),
            GslYes = true,
            GslDate = new DateOnly(2025, 2, 15)
        };

        var result = await page.OnPostCreateAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        var stored = await db.FfcRecords.AsNoTracking().SingleAsync();
        Assert.Equal(country.Id, stored.CountryId);
        Assert.Equal((short)2025, stored.Year);
        Assert.True(stored.IpaYes);
        Assert.Equal(new DateOnly(2025, 1, 10), stored.IpaDate);
        Assert.Equal("Record created.", page.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task OnPostUpdateAsync_WithInactiveExistingCountry_AllowsUpdate()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2024
        };

        db.FfcRecords.Add(record);
        await db.SaveChangesAsync();

        await db.Entry(record).ReloadAsync();
        var originalRowVersion = Convert.ToBase64String(record.RowVersion);

        country.IsActive = false;
        await db.SaveChangesAsync();

        page.Input = new ManageModel.InputModel
        {
            Id = record.Id,
            CountryId = country.Id,
            Year = 2026,
            IpaYes = true,
            IpaDate = new DateOnly(2026, 1, 1),
            RowVersion = originalRowVersion
        };

        var result = await page.OnPostUpdateAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.True(page.ModelState.IsValid);

        var updated = await db.FfcRecords.FindAsync(record.Id);
        Assert.NotNull(updated);
        Assert.Equal((short)2026, updated!.Year);
        Assert.True(updated.IpaYes);
        Assert.Equal(new DateOnly(2026, 1, 1), updated.IpaDate);
        Assert.Equal("Record updated.", page.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task OnPostUpdateAsync_WhenConcurrencyConflict_ReloadsInputWithError()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2024,
            IpaYes = true,
            IpaDate = new DateOnly(2024, 1, 15)
        };

        db.FfcRecords.Add(record);
        await db.SaveChangesAsync();

        await db.Entry(record).ReloadAsync();
        var staleRowVersion = Convert.ToBase64String(record.RowVersion);

        record.OverallRemarks = "Updated externally";
        await db.SaveChangesAsync();
        await db.Entry(record).ReloadAsync();

        page.Input = new ManageModel.InputModel
        {
            Id = record.Id,
            CountryId = country.Id,
            Year = 2026,
            IpaYes = true,
            IpaDate = new DateOnly(2026, 1, 1),
            RowVersion = staleRowVersion
        };

        var result = await page.OnPostUpdateAsync();

        Assert.IsType<PageResult>(result);
        var state = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateEntry>(page.ModelState[string.Empty]);
        Assert.NotEmpty(state.Errors);
        Assert.Contains("modified by another user", state.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(page.Input.RowVersion);
        Assert.Equal(Convert.ToBase64String(record.RowVersion), page.Input.RowVersion);
        Assert.Equal(record.Year, page.Input.Year);
        Assert.Equal(record.OverallRemarks, page.Input.OverallRemarks);
    }

    [Fact]
    public async Task UnauthorizedUser_IsRedirectedToAccessDenied()
    {
        using var factory = new FfcRecordsManagePageFactory();
        var client = CreateClientForUser(factory, "general-user");

        var response = await client.GetAsync("/ProjectOfficeReports/FFC/Records/Manage");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/Identity/Account/AccessDenied?ReturnUrl=%2FProjectOfficeReports%2FFFC%2FRecords%2FManage", location.PathAndQuery);
    }

    [Fact]
    public async Task UnauthorizedUser_SeesFriendlyMessageOnAccessDenied()
    {
        using var factory = new FfcRecordsManagePageFactory();
        var client = CreateClientForUser(factory, "general-user");

        var response = await client.GetAsync("/ProjectOfficeReports/FFC/Records/Manage");
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var accessDenied = await client.GetAsync(new Uri(client.BaseAddress!, location));

        Assert.Equal(HttpStatusCode.Forbidden, accessDenied.StatusCode);
        var content = await accessDenied.Content.ReadAsStringAsync();
        Assert.Contains("Only administrators or heads of department can manage FFC records", content, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetIpaMismatch(ManageModel.InputModel input)
    {
        input.IpaYes = false;
        input.IpaDate = new DateOnly(2024, 1, 1);
    }

    private static void SetGslMismatch(ManageModel.InputModel input)
    {
        input.GslYes = false;
        input.GslDate = new DateOnly(2024, 2, 1);
    }

    private static ManageModel CreatePage(ApplicationDbContext db)
    {
        return new ManageModel(db, new StubAuditService(), NullLogger<ManageModel>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<FfcCountry> SeedCountryAsync(ApplicationDbContext db)
    {
        var country = new FfcCountry { Name = "Testland", IsoCode = "TST" };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();
        return country;
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

    private static HttpClient CreateClientForUser(FfcRecordsManagePageFactory factory, string userId, params string[] roles)
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

    private sealed class FfcRecordsManagePageFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase($"ffc-records-manage-{Guid.NewGuid()}"));
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
}
