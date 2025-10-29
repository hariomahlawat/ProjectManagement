using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    [InlineData(nameof(FfcRecordsManagePageTests.SetDeliveryMismatch), nameof(ManageModel.Input.DeliveryDate))]
    [InlineData(nameof(FfcRecordsManagePageTests.SetInstallationMismatch), nameof(ManageModel.Input.InstallationDate))]
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
            GslDate = new DateOnly(2025, 2, 15),
            DeliveryYes = true,
            DeliveryDate = new DateOnly(2025, 3, 20),
            InstallationYes = true,
            InstallationDate = new DateOnly(2025, 4, 25)
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

    private static void SetDeliveryMismatch(ManageModel.InputModel input)
    {
        input.DeliveryYes = false;
        input.DeliveryDate = new DateOnly(2024, 3, 1);
    }

    private static void SetInstallationMismatch(ManageModel.InputModel input)
    {
        input.InstallationYes = false;
        input.InstallationDate = new DateOnly(2024, 4, 1);
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
        public Task LogAsync(string action, string? userId, string? userName, IDictionary<string, string?> data, HttpContext http)
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
