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
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records.Projects;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcProjectsManagePageTests
{
    [Fact]
    public async Task OnGetAsync_WithUnknownRecord_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var page = CreatePage(db);

        var result = await page.OnGetAsync(123, null);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostCreateAsync_WithUnknownRecord_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());
        page.Input = new ManageModel.InputModel { Name = "New Project" };

        var result = await page.OnPostCreateAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostCreateAsync_WithLinkedProject_CreatesProject()
    {
        await using var db = CreateDbContext();
        var (record, linkedProject) = await SeedRecordAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        page.Input = new ManageModel.InputModel
        {
            Name = "Simulator Upgrade",
            Remarks = "Initial scope",
            LinkedProjectId = linkedProject.Id,
            Quantity = 3,
            IsDelivered = true,
            DeliveredOn = new DateOnly(2024, 1, 15),
            IsInstalled = true,
            InstalledOn = new DateOnly(2024, 2, 20)
        };

        var result = await page.OnPostCreateAsync(record.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(record.Id.ToString(), redirect.RouteValues?["recordId"]?.ToString());

        var created = await db.FfcProjects.AsNoTracking().SingleAsync();
        Assert.Equal(record.Id, created.FfcRecordId);
        Assert.Equal("Simulator Upgrade", created.Name);
        Assert.Equal(linkedProject.Id, created.LinkedProjectId);
        Assert.Equal(3, created.Quantity);
        Assert.True(created.IsDelivered);
        Assert.Equal(new DateOnly(2024, 1, 15), created.DeliveredOn);
        Assert.True(created.IsInstalled);
        Assert.Equal(new DateOnly(2024, 2, 20), created.InstalledOn);
        Assert.Equal("Project added.", page.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task OnPostUpdateAsync_WithRemarksChange_PersistsUpdate()
    {
        await using var db = CreateDbContext();
        var (record, linkedProject) = await SeedRecordAsync(db);
        var entity = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Initial",
            Remarks = "Draft",
            LinkedProjectId = linkedProject.Id
        };
        db.FfcProjects.Add(entity);
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());
        page.Input = new ManageModel.InputModel
        {
            Id = entity.Id,
            Name = "Initial",
            Remarks = "Updated remarks",
            LinkedProjectId = null,
            Quantity = 5,
            IsDelivered = true,
            DeliveredOn = new DateOnly(2024, 3, 1),
            IsInstalled = false,
            InstalledOn = null
        };

        var result = await page.OnPostUpdateAsync(record.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(record.Id.ToString(), redirect.RouteValues?["recordId"]?.ToString());

        var refreshed = await db.FfcProjects.AsNoTracking().SingleAsync();
        Assert.Equal("Updated remarks", refreshed.Remarks);
        Assert.Null(refreshed.LinkedProjectId);
        Assert.Equal(5, refreshed.Quantity);
        Assert.True(refreshed.IsDelivered);
        Assert.Equal(new DateOnly(2024, 3, 1), refreshed.DeliveredOn);
        Assert.False(refreshed.IsInstalled);
        Assert.Equal("Project updated.", page.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task OnPostCreateAsync_WithInvalidQuantity_ReturnsValidationError()
    {
        await using var db = CreateDbContext();
        var (record, _) = await SeedRecordAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        page.Input = new ManageModel.InputModel
        {
            Name = "Invalid",
            Quantity = 0
        };

        var result = await page.OnPostCreateAsync(record.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.True(page.ModelState.ContainsKey("Quantity"));
        Assert.Empty(await db.FfcProjects.ToListAsync());
    }

    [Fact]
    public async Task OnPostCreateAsync_InstallationWithoutDelivery_ReturnsValidationError()
    {
        await using var db = CreateDbContext();
        var (record, _) = await SeedRecordAsync(db);
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        page.Input = new ManageModel.InputModel
        {
            Name = "Future install",
            Quantity = 1,
            IsInstalled = true,
            InstalledOn = new DateOnly(2024, 4, 10)
        };

        var result = await page.OnPostCreateAsync(record.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.Contains(page.ModelState, kvp => kvp.Key.Contains(nameof(ManageModel.InputModel.IsInstalled)));
        Assert.Empty(await db.FfcProjects.ToListAsync());
    }

    [Fact]
    public async Task OnPostDeleteAsync_RemovesProject()
    {
        await using var db = CreateDbContext();
        var (record, _) = await SeedRecordAsync(db);
        var project = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Legacy",
            Remarks = "To delete"
        };
        db.FfcProjects.Add(project);
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        var result = await page.OnPostDeleteAsync(record.Id, project.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(record.Id.ToString(), redirect.RouteValues?["recordId"]?.ToString());
        Assert.Empty(await db.FfcProjects.ToListAsync());
        Assert.Equal("Project removed.", page.TempData["StatusMessage"]);
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

    private static async Task<(FfcRecord Record, Project Project)> SeedRecordAsync(ApplicationDbContext db)
    {
        var country = new FfcCountry { Name = "Seedland", IsoCode = "SED" };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2023
        };
        db.FfcRecords.Add(record);

        var project = new Project
        {
            Name = "Reference",
            CreatedByUserId = "user-1"
        };
        db.Projects.Add(project);

        await db.SaveChangesAsync();
        return (record, project);
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
