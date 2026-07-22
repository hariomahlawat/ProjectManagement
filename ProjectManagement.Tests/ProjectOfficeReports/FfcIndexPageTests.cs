using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Data;
using ProjectManagement.Services.Ffc;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcIndexPageTests
{
    [Fact]
    public async Task OnGetAsync_LoadsCompactPortfolioRowsAndRelatedCounts()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Alpha", "ALP");
        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2026,
            OverallRemarks = "Ready to deploy",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.FfcRecords.Add(record);
        await db.SaveChangesAsync();

        db.FfcProjects.Add(new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Simulator build",
            Remarks = "Completed",
            Quantity = 2,
            IsDelivered = true
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

        var page = CreatePage(db);
        ConfigurePageContext(page, CreatePrincipal(isAdmin: true));

        await page.OnGetAsync(CancellationToken.None);

        Assert.True(page.CanManageRecords);
        var loaded = Assert.Single(page.Records);
        Assert.Equal(1, loaded.ProjectCount);
        Assert.Equal(1, loaded.AttachmentCount);
        Assert.Equal(2, loaded.DeliveredNotInstalledUnits);
        Assert.Equal("Ready to deploy", loaded.OverallRemarks);
        Assert.Equal(1, page.Summary.ProjectCount);
    }

    [Fact]
    public async Task OnGetAsync_WithHoDUser_AllowsManagement()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Gamma", "GAM");
        db.FfcRecords.Add(new FfcRecord { CountryId = country.Id, Year = 2025 });
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        ConfigurePageContext(page, CreatePrincipal(isHod: true));

        await page.OnGetAsync(CancellationToken.None);

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

        var page = CreatePage(db);
        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync(CancellationToken.None);

        Assert.False(page.CanManageRecords);
        Assert.Single(page.Records);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task OnGetAsync_UsesTwentyFiveRecordPagesAndKeepsSummaryGlobal()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Alpha", "ALP");

        for (short year = 2026; year >= 2000; year--)
        {
            var record = new FfcRecord { CountryId = country.Id, Year = year };
            db.FfcRecords.Add(record);
            await db.SaveChangesAsync();
            db.FfcProjects.Add(new FfcProject
            {
                FfcRecordId = record.Id,
                Name = $"Project {year}",
                Quantity = 1
            });
        }
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        page.PageNumber = 2;
        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(27, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.Records.Count);
        Assert.Equal(27, page.Summary.RecordCount);
        Assert.Equal(new short[] { 2001, 2000 }, page.Records.Select(record => record.Year).ToArray());
    }

    [Fact]
    public async Task OnGetAsync_AppliesPartialDeliveryFilter()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Alpha", "ALP");
        var partial = new FfcRecord { CountryId = country.Id, Year = 2026 };
        var pending = new FfcRecord { CountryId = country.Id, Year = 2025 };
        db.FfcRecords.AddRange(partial, pending);
        await db.SaveChangesAsync();

        db.FfcProjects.AddRange(
            new FfcProject { FfcRecordId = partial.Id, Name = "Delivered", Quantity = 1, IsDelivered = true },
            new FfcProject { FfcRecordId = partial.Id, Name = "Planned", Quantity = 1 },
            new FfcProject { FfcRecordId = pending.Id, Name = "Pending", Quantity = 1 });
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        page.DeliveryStatus = FfcFilterState.Partial;
        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync(CancellationToken.None);

        var record = Assert.Single(page.Records);
        Assert.Equal(partial.Id, record.RecordId);
        Assert.Equal(FfcCompletionState.Partial, record.DeliveryState);
    }

    [Fact]
    public async Task BuildRoute_CanRemoveOneFilterWhileRetainingOthers()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Alpha", "ALP");
        db.FfcRecords.Add(new FfcRecord { CountryId = country.Id, Year = 2026 });
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        page.Query = "simulator";
        page.Year = 2026;
        page.CountryId = country.Id;
        page.DeliveryStatus = FfcFilterState.Partial;
        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync(CancellationToken.None);

        var route = page.BuildRoute(page: 1, remove: "year");

        Assert.False(route.ContainsKey("year"));
        Assert.Equal("simulator", route["q"]);
        Assert.Equal(country.Id.ToString(), route["countryId"]);
        Assert.Equal("partial", route["delivery"]);
    }

    [Fact]
    public async Task OnGetAsync_NormalizesPartialBinaryMilestoneFilter()
    {
        await using var db = CreateDbContext();
        var country = await SeedCountryAsync(db, "Alpha", "ALP");
        db.FfcRecords.Add(new FfcRecord { CountryId = country.Id, Year = 2026 });
        await db.SaveChangesAsync();

        var page = CreatePage(db);
        page.IpaStatus = FfcFilterState.Partial;
        ConfigurePageContext(page, CreatePrincipal());

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(FfcFilterState.Any, page.IpaStatus);
        Assert.Single(page.Records);
    }

    private static IndexModel CreatePage(ApplicationDbContext db)
    {
        var progress = new StubProgressService();
        var portfolio = new FfcPortfolioService(db, progress);
        return new IndexModel(db, portfolio);
    }

    private sealed class StubProgressService : IFfcProgressService
    {
        public Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
            IReadOnlyCollection<FfcProgressTarget> targets,
            CancellationToken cancellationToken = default)
        {
            var result = targets.ToDictionary(
                target => target.FfcProjectId,
                target => new FfcProgressSnapshot(
                    target.FfcProjectId,
                    target.FfcProjectRemarks,
                    null,
                    target.LinkedProjectId.HasValue
                        ? FfcProgressSource.ExternalProjectRemark
                        : FfcProgressSource.FfcProjectRemark,
                    target.LinkedProjectId.HasValue));

            return Task.FromResult<IReadOnlyDictionary<long, FfcProgressSnapshot>>(result);
        }

        public Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<FfcCountry> SeedCountryAsync(
        ApplicationDbContext db,
        string name,
        string iso)
    {
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
