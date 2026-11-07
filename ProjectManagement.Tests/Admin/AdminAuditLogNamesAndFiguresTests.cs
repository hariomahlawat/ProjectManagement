using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;
using LogsIndexModel = ProjectManagement.Areas.Admin.Pages.Logs.IndexModel;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Third review, C6 and U5 (docs/ux/admin-review-3-fixes.md, PR 5). The audit log showed roles
/// as "Admin,HoD,Project Officer", counted events as "records changed", and its text search
/// missed "R&amp;D", apostrophes and Hindi because the details were stored as \uXXXX escapes.
/// </summary>
public sealed class AdminAuditLogNamesAndFiguresTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActorColumn_ShowsFullNameAndFullRoleNames()
    {
        await using var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "hod.cse", FullName = "Col A Sharma" });
        db.AuditLogs.Add(new AuditLog
        {
            TimeUtc = Now.UtcDateTime,
            Level = "Info",
            Action = "AdminUserUpdated",
            UserId = "user-1",
            UserName = "hod.cse",
            DataJson = Details(new Dictionary<string, string?>
            {
                ["EntityType"] = "User",
                ["EntityId"] = "user-9",
                ["ActorUserId"] = "user-1",
                ["ActorName"] = "hod.cse",
                ["ActorRoles"] = "Admin,HoD,Project Officer"
            })
        });
        await db.SaveChangesAsync();

        var page = LogsPage(db);
        await page.OnGetAsync(default);

        var row = Assert.Single(page.Result.Rows);
        Assert.Equal("Col A Sharma", row.Actor);
        Assert.Equal("Administrator, Head of Department, Project Officer", page.ActorRoles(row));

        using var detail = JsonDocument.Parse(page.DetailJson(row));
        Assert.Equal("Col A Sharma", detail.RootElement.GetProperty("actor").GetString());
        Assert.Equal(
            "Administrator, Head of Department, Project Officer",
            detail.RootElement.GetProperty("actorRoles").GetString());
    }

    [Fact]
    public async Task ActorRoles_IsEmptyWhenTheRowKeptNoRoles()
    {
        await using var db = NewDb();
        db.AuditLogs.Add(new AuditLog { TimeUtc = Now.UtcDateTime, Level = "Info", Action = "Job", DataJson = "{}" });
        await db.SaveChangesAsync();

        var page = LogsPage(db);
        await page.OnGetAsync(default);

        Assert.Null(page.ActorRoles(Assert.Single(page.Result.Rows)));
    }

    [Fact]
    public async Task RecordsChanged_CountsDistinctRecordsNotEvents()
    {
        await using var db = NewDb();
        // Three events on one project, one on a user with the same id, one on another project,
        // and one event that names no record.
        db.AuditLogs.AddRange(
            Event("Project", "42"),
            Event("Project", "42"),
            Event("Project", "42"),
            Event("User", "42"),
            Event("Project", "7"),
            Event("Job", null),
            new AuditLog { TimeUtc = Now.UtcDateTime, Level = "Info", Action = "Plain" });
        await db.SaveChangesAsync();

        var page = LogsPage(db);
        await page.OnGetAsync(default);

        Assert.Equal(7, page.Result.Summary!.Total);
        Assert.Equal(3, page.Result.Summary.AffectedRecords);
        var people = Assert.Single(page.Metrics, metric => metric.Label is "People" or "Person");
        Assert.Equal("3 records changed", people.Detail);
    }

    [Fact]
    public void DistinctRecordKeys_TranslatesToPostgreSql()
    {
        // Built with the real provider; ToQueryString does not open the connection.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused")
            .Options);

        var sql = AdminLogQueryService.DistinctRecordKeys(db.AuditLogs).ToQueryString();

        Assert.Contains("DISTINCT", sql);
        Assert.Contains("strpos", sql);
    }

    [Theory]
    [InlineData("R&D")]
    [InlineData("officer's")]
    [InlineData("परियोजना")]
    public async Task NewEntries_AreFoundBySearchForAmpersandApostropheAndHindi(string term)
    {
        await using var db = NewDb();
        var audit = new AuditService(db, new HttpContextAccessor(), FakeClock.AtUtc(Now));
        var admin = new AdminAuditService(audit, new HttpContextAccessor());
        await admin.RecordAsync(new AdminAuditEntry(
            "MasterDataUpdated",
            "Lookup",
            "5",
            Before: new { Name = "Old" },
            After: new { Name = "R&D wing, officer's note: परियोजना" },
            Reason: "Renamed for R&D"));

        var stored = Assert.Single(db.AuditLogs).DataJson!;
        Assert.Contains("R&D", stored);
        Assert.Contains("officer's", stored);
        Assert.Contains("परियोजना", stored);

        var service = new AdminLogQueryService(db, new AdminTimeService(FakeClock.AtUtc(Now)));
        var result = await service.GetAsync(new AdminLogQuery(
            Level: null, Action: null, UserName: null, UserId: null, Ip: null,
            Contains: term, From: null, To: null));

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task UnescapedDetails_StayEscapedWhereThePageEmbedsThem()
    {
        await using var db = NewDb();
        var audit = new AuditService(db, new HttpContextAccessor(), FakeClock.AtUtc(Now));
        await audit.LogAsync(
            "MasterDataUpdated",
            data: new Dictionary<string, string?>
            {
                ["EntityType"] = "Lookup",
                ["EntityId"] = "5",
                ["Reason"] = "</script><script>alert('x')</script> R&D"
            });

        var page = LogsPage(db);
        await page.OnGetAsync(default);
        var row = Assert.Single(page.Result.Rows);

        // The drawer's JSON goes into a <script type="application/json"> block with Html.Raw.
        var embedded = page.DetailJson(row);
        Assert.DoesNotContain("<", embedded);
        Assert.DoesNotContain(">", embedded);
        Assert.DoesNotContain("'", embedded);

        using var detail = JsonDocument.Parse(embedded);
        Assert.Equal("</script><script>alert('x')</script> R&D", detail.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void OlderEscapedRows_StillReadAsWritten()
    {
        var parser = new AdminAuditPayloadParser();
        var payload = parser.Parse("{\"EntityType\":\"Lookup\",\"Reason\":\"R\\u0026D \\u092A\"}");

        Assert.Equal("R&D प", payload.Reason);
        Assert.Contains("R&D प", payload.RawPrettyJson);
    }

    private static AuditLog Event(string entityType, string? entityId) => new()
    {
        TimeUtc = Now.UtcDateTime,
        Level = "Info",
        Action = "Changed",
        UserId = "user-1",
        DataJson = Details(new Dictionary<string, string?>
        {
            ["EntityType"] = entityType,
            ["EntityId"] = entityId
        })
    };

    private static string Details(Dictionary<string, string?> data) =>
        JsonSerializer.Serialize(data, AuditService.DetailsJsonOptions);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static LogsIndexModel LogsPage(ApplicationDbContext db)
    {
        var time = new AdminTimeService(FakeClock.AtUtc(Now));
        var page = new LogsIndexModel(
            new AdminLogQueryService(db, time),
            new AuditActionPresentationCatalog(),
            new AdminClientDescriptorService(),
            new NoEntityLinks(),
            new SafeCsvWriter(),
            time,
            new AdminRoleDescriptorCatalog())
        {
            All = true
        };
        // Url.Page("./Index") resolves against the current page's route values.
        var routeData = new RouteData();
        routeData.Values["page"] = "/Logs/Index";
        routeData.Values["area"] = "Admin";
        page.PageContext = new PageContext(new ActionContext(
            new DefaultHttpContext(),
            routeData,
            new CompiledPageActionDescriptor()));
        page.Url = new NoUrls(page.PageContext);
        return page;
    }

    private sealed class NoUrls : IUrlHelper
    {
        public NoUrls(ActionContext context) => ActionContext = context;
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext actionContext) => "#";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "#";
        public string? RouteUrl(UrlRouteContext routeContext) => "#";
    }

    private sealed class NoEntityLinks : IAdminAuditEntityLinkResolver
    {
        public AdminAuditEntityLink? Resolve(HttpContext httpContext, AdminAuditPayload payload) => null;
    }
}
