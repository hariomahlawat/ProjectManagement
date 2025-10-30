using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcIndexPageTests
{
    [Fact]
    public async Task OnGetAsync_WithAdminUser_LoadsRelatedData()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Alpha", "ALP");
        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2024,
            OverallRemarks = "Ready to deploy"
        };

        db.FfcRecords.Add(record);
        await db.SaveChangesAsync();

        db.FfcProjects.Add(new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Simulator build",
            Remarks = "Completed"
        });

        db.FfcAttachments.Add(new FfcAttachment
        {
            FfcRecordId = record.Id,
            Kind = FfcAttachmentKind.Pdf,
            FilePath = "alpha.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            UploadedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        var page = new IndexModel(db);
        ConfigurePageContext(page, CreatePrincipal(isAdmin: true));

        await page.OnGetAsync();

        Assert.True(page.CanManageRecords);
        var loaded = Assert.Single(page.Records);
        Assert.Equal(1, loaded.Projects.Count);
        Assert.Equal(1, loaded.Attachments.Count);
    }

    [Fact]
    public async Task OnGetAsync_WithHoDUser_AllowsManagement()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Gamma", "GAM");

        db.FfcRecords.Add(new FfcRecord
        {
            CountryId = country.Id,
            Year = 2025
        });

        await db.SaveChangesAsync();

        var page = new IndexModel(db);
        ConfigurePageContext(page, CreatePrincipal(isHod: true));

        await page.OnGetAsync();

        Assert.True(page.CanManageRecords);
    }

    [Fact]
    public async Task OnGetAsync_ExcludesSoftDeletedRecords()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Beta", "BET");

        db.FfcRecords.AddRange(
            new FfcRecord { CountryId = country.Id, Year = 2023 },
            new FfcRecord { CountryId = country.Id, Year = 2022, IsDeleted = true });

        await db.SaveChangesAsync();

        var page = new IndexModel(db);
        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync();

        Assert.False(page.CanManageRecords);
        Assert.Single(page.Records);
        Assert.All(page.Records, record => Assert.False(record.IsDeleted));
    }

    [Fact]
    public async Task OnGetAsync_AppliesPagingAndSearch()
    {
        await using var db = CreateDbContext();
        var alpha = await SeedCountryAsync(db, "Alpha", "ALP");
        var beta = await SeedCountryAsync(db, "Beta", "BET");

        db.FfcRecords.AddRange(
            new FfcRecord { CountryId = alpha.Id, Year = 2026 },
            new FfcRecord { CountryId = alpha.Id, Year = 2025 });

        for (short year = 2027; year >= 2016; year--)
        {
            db.FfcRecords.Add(new FfcRecord { CountryId = beta.Id, Year = year });
        }

        await db.SaveChangesAsync();

        var page = new IndexModel(db)
        {
            Query = "beta",
            PageNumber = 2
        };

        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync();

        Assert.Equal(12, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.Records.Count);
        Assert.All(page.Records, record => Assert.Equal(beta.Id, record.CountryId));
        var years = page.Records.Select(r => r.Year).OrderByDescending(y => y).ToArray();
        Assert.Equal(new[] { (short)2017, (short)2016 }, years);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<FfcCountry> SeedCountryAsync(ApplicationDbContext db, string name, string iso)
    {
        var existing = await db.FfcCountries.FirstOrDefaultAsync(c => c.IsoCode == iso);
        if (existing is not null)
        {
            return existing;
        }

        var country = new FfcCountry { Name = name, IsoCode = iso };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();
        return country;
    }

    private static ClaimsPrincipal CreatePrincipal(bool isAdmin = false, bool isHod = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user"),
            new(ClaimTypes.Name, "user")
        };

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        if (isHod)
        {
            claims.Add(new Claim(ClaimTypes.Role, "HoD"));
        }

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
    }
}
