using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.AccessGovernance;
using ProjectManagement.Services.Usage;
using ProjectManagement.Tests.Fakes;
using Xunit;
using AccessGovernanceIndexModel = ProjectManagement.Areas.Admin.Pages.AccessGovernance.IndexModel;
using LoginsModel = ProjectManagement.Areas.Admin.Pages.Analytics.LoginsModel;
using LogsIndexModel = ProjectManagement.Areas.Admin.Pages.Logs.IndexModel;
using UsageIndexModel = ProjectManagement.Pages.Usage.IndexModel;
using UsersIndexModel = ProjectManagement.Areas.Admin.Pages.Users.IndexModel;

namespace ProjectManagement.Tests.Admin;

// Admin fix PR 4 (second review): every CSV export of personal data leaves one audit record,
// and a cut-off export says so in its last row as well as in the X-PRISM-Export-Truncated header.
public sealed class AdminExportAuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UsersExport_WritesOneEvent_WithFiltersAndCount()
    {
        var exports = new RecordingExportAudit();
        var page = WithContext(new UsersIndexModel(
            FakeUserQueries(3), new SafeCsvWriter(), Time()));
        page.Q = "sharma";
        page.Role = "Admin";

        var result = await page.OnGetExportAsync(exports, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        var record = Assert.Single(exports.Records);
        Assert.Same(AdminExportKind.Users, record.Kind);
        Assert.Equal(3, record.RowCount);
        Assert.False(record.Truncated);
        Assert.Equal("sharma", record.Filters["Search"]);
        Assert.Equal("Admin", record.Filters["Role"]);
    }

    [Fact]
    public async Task AccessReviewExport_WritesOneEvent_WithCount()
    {
        var exports = new RecordingExportAudit();
        var page = WithContext(new AccessGovernanceIndexModel(
            new FakeGovernance(4), new SafeCsvWriter(), Time(), new AdminRoleDescriptorCatalog()));

        var result = await page.OnGetExportAsync(exports, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        var record = Assert.Single(exports.Records);
        Assert.Same(AdminExportKind.AccessReview, record.Kind);
        Assert.Equal(4, record.RowCount);
        Assert.Empty(record.Filters);
    }

    [Fact]
    public async Task AuditLogExport_WritesOneEvent_WithFiltersAndCount_AndNoNoticeWhenComplete()
    {
        var exports = new RecordingExportAudit();
        var page = LogsPage(new FakeLogQueries(5));
        page.Level = "Warning";
        page.Contains = "export";

        var result = (FileContentResult)await page.OnGetExportCsvAsync(exports, CancellationToken.None);

        var record = Assert.Single(exports.Records);
        Assert.Same(AdminExportKind.AuditLog, record.Kind);
        Assert.Equal(5, record.RowCount);
        Assert.False(record.Truncated);
        Assert.Equal("Warning", record.Filters["Severity"]);
        Assert.Equal("export", record.Filters["Contains"]);
        Assert.False(page.Response.Headers.ContainsKey("X-PRISM-Export-Truncated"));
        Assert.DoesNotContain("Export stopped at", Csv(result));
        Assert.Equal(1 + 5, CsvLines(result).Length);
    }

    [Fact]
    public async Task AuditLogExport_WhenCut_EndsWithThePlainNoticeRow_AndKeepsTheHeader()
    {
        var exports = new RecordingExportAudit();
        var page = LogsPage(new FakeLogQueries(50_001));

        var result = (FileContentResult)await page.OnGetExportCsvAsync(exports, CancellationToken.None);

        Assert.Equal("true", page.Response.Headers["X-PRISM-Export-Truncated"].ToString());
        Assert.Equal(
            "\"Export stopped at 50,000 rows; narrow the filters to get the rest.\"",
            CsvLines(result).Last());
        var record = Assert.Single(exports.Records);
        Assert.Equal(50_000, record.RowCount);
        Assert.True(record.Truncated);
    }

    [Fact]
    public async Task SignInExport_WritesOneEvent_WithFiltersAndCount()
    {
        var exports = new RecordingExportAudit();
        var page = LoginsPage(new FakeLoginMonitoring(rows: 2, truncated: false));
        page.Outcome = "failed";

        var result = (FileContentResult)await page.OnGetExportCsvAsync(exports, CancellationToken.None);

        var record = Assert.Single(exports.Records);
        Assert.Same(AdminExportKind.SignIns, record.Kind);
        Assert.Equal(2, record.RowCount);
        Assert.False(record.Truncated);
        Assert.Equal("failed", record.Filters["Outcome"]);
        Assert.Equal("30", record.Filters["Days"]);
        Assert.DoesNotContain("Export stopped at", Csv(result));
    }

    [Fact]
    public async Task SignInExport_WhenCut_EndsWithThePlainNoticeRow_AndKeepsTheHeader()
    {
        var exports = new RecordingExportAudit();
        var page = LoginsPage(new FakeLoginMonitoring(rows: 2, truncated: true));

        var result = (FileContentResult)await page.OnGetExportCsvAsync(exports, CancellationToken.None);

        Assert.Equal("true", page.Response.Headers["X-PRISM-Export-Truncated"].ToString());
        Assert.Equal(
            "\"Export stopped at 2 rows; narrow the filters to get the rest.\"",
            CsvLines(result).Last());
        Assert.True(Assert.Single(exports.Records).Truncated);
    }

    [Fact]
    public async Task ErpUsageExport_WritesOneEvent_WithFiltersAndCount()
    {
        var exports = new RecordingExportAudit();
        var page = WithContext(new UsageIndexModel(FakeUsage(6), Time(), new ErpUsageModuleCatalog()));
        page.Module = "projects";

        var result = await page.OnGetExportAsync(exports, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        var record = Assert.Single(exports.Records);
        Assert.Same(AdminExportKind.ErpUsage, record.Kind);
        Assert.Equal(6, record.RowCount);
        Assert.Equal("projects", record.Filters["Module"]);
    }

    // Like every other admin action, a failed audit write is not swallowed: the request fails and
    // no file is handed out without its audit record.
    [Fact]
    public async Task FailedAuditWrite_StopsTheExport()
    {
        var page = WithContext(new UsersIndexModel(FakeUserQueries(2), new SafeCsvWriter(), Time()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => page.OnGetExportAsync(new ThrowingExportAudit(), CancellationToken.None));
    }

    [Fact]
    public async Task Service_WritesThroughTheAdminAuditPath_ByTheSignedInUser_WithOnlyTheFiltersSet()
    {
        var audit = new RecordingAudit();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "admin-1"), new Claim(ClaimTypes.Name, "admin") },
                "Test"))
        };
        var service = new AdminExportAuditService(
            new AdminAuditService(audit, new HttpContextAccessor { HttpContext = http }));

        await service.RecordAsync(new AdminExportRecord(
            AdminExportKind.AuditLog,
            50_000,
            new Dictionary<string, string?> { ["Severity"] = "Error", ["User"] = " ", ["Ip"] = null },
            Truncated: true));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("AdminExport.AuditLog", entry.Action);
        Assert.Equal("admin-1", entry.UserId);
        Assert.Equal("Audit log exported: 50,000 rows, stopped at the export limit.", entry.Message);
        Assert.Equal(AdminExportAuditService.EntityType, entry.Data["EntityType"]);

        using var after = JsonDocument.Parse(entry.Data["After"]!);
        Assert.Equal(50_000, after.RootElement.GetProperty("rows").GetInt32());
        Assert.True(after.RootElement.GetProperty("truncated").GetBoolean());
        var filters = after.RootElement.GetProperty("filters");
        Assert.Equal("Error", filters.GetProperty("Severity").GetString());
        Assert.Single(filters.EnumerateObject());
    }

    [Fact]
    public void AuditLog_ShowsEachExportInPlainWords()
    {
        var catalog = new AuditActionPresentationCatalog();

        foreach (var kind in AdminExportKind.All)
        {
            var presentation = catalog.Describe(kind.Action, "Info");
            Assert.Equal(kind.Title, presentation.Label);
            Assert.Equal("Exports", presentation.Category);
        }

        Assert.Equal("Users exported", catalog.Describe("AdminExport.Users").Label);
        Assert.Equal("Sign-in activity exported", catalog.Describe("AdminExport.SignIns").Label);
    }

    private static AdminTimeService Time() => new(FakeClock.AtUtc(Now));

    private static LogsIndexModel LogsPage(IAdminLogQueryService queries) => WithContext(new LogsIndexModel(
        queries,
        new AuditActionPresentationCatalog(),
        new AdminClientDescriptorService(),
        Stub<IAdminAuditEntityLinkResolver>("Resolve", () => null),
        new SafeCsvWriter(),
        Time()));

    private static LoginsModel LoginsPage(IAdminLoginMonitoringService monitoring) => WithContext(new LoginsModel(
        monitoring,
        new AdminClientDescriptorService(),
        Time(),
        new SafeCsvWriter(),
        Options.Create(new AdminLoginMonitoringOptions())));

    private static T WithContext<T>(T page) where T : PageModel
    {
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        return page;
    }

    private static string Csv(FileContentResult result) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(result.FileContents).TrimStart('﻿');

    private static string[] CsvLines(FileContentResult result) =>
        Csv(result).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    private sealed class RecordingExportAudit : IAdminExportAuditService
    {
        public List<AdminExportRecord> Records { get; } = new();

        public Task RecordAsync(AdminExportRecord export, CancellationToken cancellationToken = default)
        {
            Records.Add(export);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingExportAudit : IAdminExportAuditService
    {
        public Task RecordAsync(AdminExportRecord export, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The audit log could not be written.");
    }

    private static IAdminUserQueryService FakeUserQueries(int rows) =>
        Stub<IAdminUserQueryService>(
            nameof(IAdminUserQueryService.GetForExportAsync),
            () => Task.FromResult<IReadOnlyList<AdminUserRow>>(Enumerable.Range(1, rows)
                .Select(i => new AdminUserRow(
                    $"u-{i}", $"user{i}", $"User {i}", "Maj", new[] { "Admin" },
                    new AdminUserAccountStateInfo(AdminUserAccountState.Active, "Active", "bg-success", true),
                    null, i, Now.UtcDateTime, false))
                .ToArray()));

    private static IErpUsageQueryService FakeUsage(int rows) =>
        Stub<IErpUsageQueryService>(
            nameof(IErpUsageQueryService.GetExportRowsAsync),
            () => Task.FromResult<IReadOnlyList<ErpUsageUserRow>>(Enumerable.Range(1, rows)
                .Select(i => new ErpUsageUserRow(
                    $"u-{i}", $"user{i}", $"User {i}", "Maj", new[] { "Admin" }, "Active",
                    UserAccountKind.Human, new DateOnly(2026, 10, 1), true, Now.UtcDateTime,
                    10, 20, 50, 120, new[] { "Projects" }, 5, 1, "Regular", "success",
                    Array.Empty<ErpUsageHeatmapCell>()))
                .ToArray()));

    // Answers one method of the interface; anything else the page calls is a test failure.
    private static T Stub<T>(string method, Func<object?> result) where T : class
    {
        var stub = DispatchProxy.Create<T, StubProxy>();
        ((StubProxy)(object)stub).Handler = (target, _) => target.Name == method
            ? result()
            : throw new NotSupportedException($"{target.Name} is not used by these tests.");
        return stub;
    }

    public class StubProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } =
            (target, _) => throw new NotSupportedException(target.Name);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    private sealed class FakeGovernance : IAdminAccessGovernanceService
    {
        private readonly int _users;

        public FakeGovernance(int users) => _users = users;

        public Task<AccessGovernanceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var users = Enumerable.Range(1, _users)
                .Select(i => new AccessGovernanceUserRow(
                    $"u-{i}", $"user{i}", $"User {i}", "Maj", false, false, false, false, null,
                    new[] { "Admin" }, new[] { "Admin" }, Array.Empty<AccessGovernanceFinding>()))
                .ToArray();
            return Task.FromResult(new AccessGovernanceSnapshot(
                Array.Empty<AccessGovernanceRoleRow>(),
                Array.Empty<AdminCapabilityDescriptor>(),
                users,
                users,
                Array.Empty<AccessGovernanceFinding>(),
                users.Length, users.Length, 0, 0, 0, 0));
        }
    }

    private sealed class FakeLogQueries : IAdminLogQueryService
    {
        private readonly int _rows;

        public FakeLogQueries(int rows) => _rows = rows;

        public Task<IReadOnlyList<AdminLogRow>> GetForExportAsync(
            AdminLogQuery request, int maximumRows, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminLogRow>>(Enumerable.Range(1, Math.Min(_rows, maximumRows))
                .Select(i => new AdminLogRow(Now.UtcDateTime, "Info", "AdminUserUpdated", "u-1", "admin", null, null, null, i))
                .ToArray());

        public Task<AdminLogResult> GetAsync(AdminLogQuery request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLoginMonitoring : IAdminLoginMonitoringService
    {
        private readonly int _rows;
        private readonly bool _truncated;

        public FakeLoginMonitoring(int rows, bool truncated)
        {
            _rows = rows;
            _truncated = truncated;
        }

        public Task<AdminLoginMonitoringSnapshot> GetAsync(
            AdminLoginMonitoringRequest request, CancellationToken cancellationToken = default)
        {
            var points = Enumerable.Range(1, _rows)
                .Select(i => new AdminLoginEventRow(
                    Now.AddMinutes(-i), Now.AddMinutes(-i), "u-1", "user1", "User 1",
                    AdminLoginOutcome.Failed, "10.0.0.1", null, 600, false, string.Empty,
                    AdminLoginReviewLevel.Informational, 1))
                .ToArray();
            return Task.FromResult(new AdminLoginMonitoringSnapshot(
                30, new DateOnly(2026, 10, 9), new DateOnly(2026, 11, 7),
                TimeSpan.FromHours(8), TimeSpan.FromHours(18), true,
                0, _rows, 0, 1, 1, 1, 0, 600, 600, _truncated,
                Array.Empty<AdminLoginUserOption>(),
                Array.Empty<AdminLoginTrendPoint>(),
                points, points, Array.Empty<AdminLoginEventRow>(),
                0, 1, 25));
        }
    }
}
