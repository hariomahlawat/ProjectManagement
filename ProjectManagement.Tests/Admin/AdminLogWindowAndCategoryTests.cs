using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;
using LogsIndexModel = ProjectManagement.Areas.Admin.Pages.Logs.IndexModel;

namespace ProjectManagement.Tests.Admin;

public sealed class AdminLogWindowAndCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);

    // Maintenance's "Audit history" buttons filtered on Action = "PdfIngestion" /
    // "LegacyImport", an exact match no audit row has, so they always showed nothing.
    [Theory]
    [InlineData("pdf-ingestion", 2)]
    [InlineData("legacy-import", 1)]
    public async Task MaintenanceCategories_FindEveryStageOfTheRun(string category, int expected)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.AuditLogs.AddRange(
            new AuditLog { TimeUtc = Now.AddHours(-3).UtcDateTime, Level = "Info", Action = "PdfIngestionStarted" },
            new AuditLog { TimeUtc = Now.AddHours(-2).UtcDateTime, Level = "Info", Action = "PdfIngestionCompleted" },
            new AuditLog { TimeUtc = Now.AddHours(-1).UtcDateTime, Level = "Info", Action = "LegacyImportCommitted" },
            new AuditLog { TimeUtc = Now.AddHours(-1).UtcDateTime, Level = "Info", Action = "AdminUserUpdated" });
        await db.SaveChangesAsync();

        var service = new AdminLogQueryService(db, new AdminTimeService(FakeClock.AtUtc(Now)));
        var result = await service.GetAsync(new AdminLogQuery(null, null, null, null, null, null, null, null, Category: category));

        Assert.Equal(expected, result.Total);
    }

    // The unfiltered page counted, grouped and charted the whole audit history on every load.
    [Fact]
    public void UnfilteredPage_DefaultsToTheLastThirtyDays()
    {
        var page = CreatePage();

        Assert.True(page.UsesDefaultWindow);
        Assert.Equal(new DateOnly(2026, 10, 8), BuildQuery(page).From);
    }

    [Fact]
    public void AllHistory_OrANarrowingFilter_TurnsTheWindowOff()
    {
        var all = CreatePage();
        all.All = true;
        var forUser = CreatePage();
        forUser.UserId = "u-1";
        var bySeverity = CreatePage();
        bySeverity.Level = "Error";

        Assert.Null(BuildQuery(all).From);
        Assert.Null(BuildQuery(forUser).From);
        // Severity and category alone still cover the whole log, so they keep the window.
        Assert.True(bySeverity.UsesDefaultWindow);
    }

    // Typed searches scan every row ("contains"), so they keep the window until the
    // administrator asks for all history (docs/ux/admin-review-2-fixes.md, PR 11).
    [Theory]
    [InlineData("text")]
    [InlineData("name")]
    [InlineData("ip")]
    public void TypedSearches_KeepTheThirtyDayWindow(string field)
    {
        var page = CreatePage();
        switch (field)
        {
            case "text": page.Contains = "budget"; break;
            case "name": page.UserName = "rao"; break;
            default: page.Ip = "172.24"; break;
        }

        Assert.True(page.UsesDefaultWindow);
        Assert.Equal(new DateOnly(2026, 10, 8), BuildQuery(page).From);

        page.All = true;
        Assert.Null(BuildQuery(page).From);
    }

    private static LogsIndexModel CreatePage() =>
        new(
            Unused<IAdminLogQueryService>(),
            Unused<IAuditActionPresentationCatalog>(),
            Unused<IAdminClientDescriptorService>(),
            Unused<IAdminAuditEntityLinkResolver>(),
            Unused<ISafeCsvWriter>(),
            new AdminTimeService(FakeClock.AtUtc(Now)),
            Unused<IAdminRoleDescriptorCatalog>());

    private static AdminLogQuery BuildQuery(LogsIndexModel page) =>
        (AdminLogQuery)typeof(LogsIndexModel)
            .GetMethod("BuildQuery", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, null)!;

    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} is not used by these tests.");
    }
}
