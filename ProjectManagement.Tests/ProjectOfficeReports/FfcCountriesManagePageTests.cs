using System;
using System.Collections.Generic;
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
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Countries;
using ProjectManagement.Data;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcCountriesManagePageTests
{
    [Fact]
    public async Task OnPostCreateAsync_WithValidInput_CreatesCountry()
    {
        await using var db = CreateDbContext();
        var page = CreatePage(db);
        page.Input = new ManageModel.InputModel
        {
            Name = "  India  ",
            IsoCode = " ind "
        };

        ConfigurePageContext(page, CreateAdminPrincipal());

        var result = await page.OnPostCreateAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        var routeValues = Assert.NotNull(redirect.RouteValues);
        Assert.Equal("1", routeValues["page"] as string);

        var created = await db.FfcCountries.AsNoTracking().SingleAsync();
        Assert.Equal("India", created.Name);
        Assert.Equal("IND", created.IsoCode);
        Assert.NotNull(created.RowVersion);
        Assert.True(created.RowVersion.Length > 0);
        Assert.Equal("Country created.", page.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task OnPostUpdateAsync_WithDuplicateName_AddsModelError()
    {
        await using var db = CreateDbContext();
        db.FfcCountries.AddRange(
            new Areas.ProjectOfficeReports.Domain.FfcCountry { Name = "India" },
            new Areas.ProjectOfficeReports.Domain.FfcCountry { Name = "Japan" });
        await db.SaveChangesAsync();

        var target = await db.FfcCountries.AsNoTracking().SingleAsync(x => x.Name == "Japan");

        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());
        page.Input = new ManageModel.InputModel
        {
            Id = target.Id,
            Name = "India",
            IsoCode = "JPN",
            IsActive = true,
            RowVersion = Convert.ToBase64String(target.RowVersion)
        };

        var result = await page.OnPostUpdateAsync();

        var pageResult = Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.ContainsKey("Input.Name"));
        Assert.Null(page.TempData["StatusMessage"]);
        Assert.True(page.Countries.Count >= 2);
    }

    [Fact]
    public async Task OnPostCreateAsync_WithInvalidIso_AddsModelError()
    {
        await using var db = CreateDbContext();
        var page = CreatePage(db);
        page.Input = new ManageModel.InputModel
        {
            Name = "Nepal",
            IsoCode = "N3P"
        };

        ConfigurePageContext(page, CreateAdminPrincipal());

        var result = await page.OnPostCreateAsync();

        var pageResult = Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.ContainsKey("Input.IsoCode"));
        Assert.Equal(0, await db.FfcCountries.CountAsync());
    }

    [Fact]
    public async Task OnPostUpdateAsync_WithStaleRowVersion_ReturnsConcurrencyMessage()
    {
        await using var db = CreateDbContext();
        var country = new Areas.ProjectOfficeReports.Domain.FfcCountry
        {
            Name = "Germany",
            IsoCode = "DEU"
        };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var originalRowVersion = Convert.ToBase64String(country.RowVersion);

        country.Name = "Deutschland";
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());
        page.Input = new ManageModel.InputModel
        {
            Id = country.Id,
            Name = "Germany",
            IsoCode = "DEU",
            IsActive = true,
            RowVersion = originalRowVersion
        };

        var result = await page.OnPostUpdateAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.TryGetValue(string.Empty, out var entry));
        var message = Assert.Single(entry.Errors).ErrorMessage;
        Assert.Contains("updated by someone else", message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(page.Input.RowVersion);
        Assert.NotEqual(originalRowVersion, page.Input.RowVersion);
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
}
