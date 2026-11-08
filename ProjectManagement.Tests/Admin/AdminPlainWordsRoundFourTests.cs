using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Pages.Diagnostics;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.AccessGovernance;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Fourth review, PR 9 (docs/ux/admin-review-4-fixes.md): the role-less accounts are named and
/// can be listed in Users, the last formal wording is plain, and three figures agree with
/// their captions.
/// </summary>
public sealed class AdminPlainWordsRoundFourTests
{
    // SECTION: U6, the accounts with no role

    [Fact]
    public async Task WhoHasWhichRole_NamesEachAccountWithNoRole()
    {
        await using var db = NewDb();
        var admin = Role("role-admin", RoleNames.Admin);
        db.Roles.Add(admin);
        db.Users.AddRange(
            User("admin", "Hari Om", "hari"),
            User("system", "System", "system"),
            User("nobody", string.Empty, "nobody"));
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "admin", RoleId = admin.Id });
        await db.SaveChangesAsync();

        var snapshot = await new AdminAccessGovernanceService(
            db, new AdminRoleDescriptorCatalog(), new AdminCapabilityCatalog(), Time()).GetSnapshotAsync();

        var finding = Assert.Single(snapshot.Findings, item => item.Code == "roleless-users");
        Assert.Equal("2 accounts have no role", finding.Title);
        Assert.DoesNotContain("user account", finding.Title, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(finding.Accounts);
        Assert.Equal(new[] { "nobody", "system" }, finding.Accounts!.Select(account => account.UserId));
        // A person's name when the account has one, the user name otherwise.
        Assert.Equal(new[] { "nobody", "System" }, finding.Accounts.Select(account => account.DisplayName));
        Assert.Equal(snapshot.UsersWithoutRoles, finding.Accounts.Count);
    }

    [Fact]
    public async Task WhoHasWhichRole_SaysOneAccountInTheSingular()
    {
        await using var db = NewDb();
        db.Users.Add(User("system", "System", "system"));
        await db.SaveChangesAsync();

        var snapshot = await new AdminAccessGovernanceService(
            db, new AdminRoleDescriptorCatalog(), new AdminCapabilityCatalog(), Time()).GetSnapshotAsync();

        var finding = Assert.Single(snapshot.Findings, item => item.Code == "roleless-users");
        Assert.Equal("1 account has no role", finding.Title);
        Assert.StartsWith("It can sign in", finding.Description);
    }

    [Fact]
    public void WhoHasWhichRole_LinksEachAccountAndTheUsersFilter()
    {
        var view = Read("Areas/Admin/Pages/AccessGovernance/Index.cshtml");

        Assert.Contains("data-roleless-accounts", view);
        Assert.Contains("href=\"@Model.UserUrl(account.UserId)\"", view);
        Assert.Contains("asp-route-role=\"@ProjectManagement.Services.Admin.AdminUserListRequest.NoRole\"", view);
        Assert.Contains(".admin-governance-finding__accounts", Read("wwwroot/css/admin.css"));
    }

    [Fact]
    public async Task UsersNoRoleFilter_ShowsExactlyTheAccountsWithNoRole()
    {
        await using var db = NewDb();
        var admin = Role("role-admin", RoleNames.Admin);
        var po = Role("role-po", RoleNames.ProjectOfficer);
        db.Roles.AddRange(admin, po);
        db.Users.AddRange(
            User("admin", "Hari Om", "hari"),
            User("po", "Project Officer", "po"),
            User("system", "System", "system"),
            User("nobody", "Nobody Yet", "nobody"));
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = "admin", RoleId = admin.Id },
            new IdentityUserRole<string> { UserId = "po", RoleId = po.Id });
        await db.SaveChangesAsync();

        var service = UserQueries(db);
        var roleless = await service.GetAsync(new AdminUserListRequest(null, AdminUserListRequest.NoRole, null));

        Assert.Equal(new[] { "nobody", "system" }, roleless.Rows.Select(row => row.Id).OrderBy(id => id));
        Assert.All(roleless.Rows, row => Assert.Empty(row.Roles));
        Assert.Equal(2, roleless.Summary.Total);

        // The same accounts Who has which role names.
        var snapshot = await new AdminAccessGovernanceService(
            db, new AdminRoleDescriptorCatalog(), new AdminCapabilityCatalog(), Time()).GetSnapshotAsync();
        var named = snapshot.Findings.Single(item => item.Code == "roleless-users").Accounts!;
        Assert.Equal(named.Select(account => account.UserId).OrderBy(id => id), roleless.Rows.Select(row => row.Id).OrderBy(id => id));

        // The value is read case-insensitively, and exports use the same filter.
        var upper = await service.GetAsync(new AdminUserListRequest(null, "NO-ROLE", null));
        Assert.Equal(2, upper.Total);
        Assert.Equal(2, (await service.GetForExportAsync(new AdminUserListRequest(null, "no-role", null))).Count);

        // Existing role values keep working.
        var admins = await service.GetAsync(new AdminUserListRequest(null, RoleNames.Admin, null));
        Assert.Equal("admin", Assert.Single(admins.Rows).Id);
        var everyone = await service.GetAsync(new AdminUserListRequest(null, null, null));
        Assert.Equal(4, everyone.Total);
    }

    [Fact]
    public void UsersRoleFilter_OffersNoRole_AndNamesItInTheChip()
    {
        var view = Read("Areas/Admin/Pages/Users/Index.cshtml");

        Assert.Contains("<option value=\"\">All roles</option>", view);
        Assert.Contains("<option value=\"@ProjectManagement.Services.Admin.AdminUserListRequest.NoRole\">No role</option>", view);
        Assert.Contains("AdminUserListRequest.IsNoRole(Model.Role) ? \"No role\"", view);
        Assert.Equal("no-role", AdminUserListRequest.NoRole);
        Assert.DoesNotContain(RoleNames.AssignableRoles.Concat(RoleNames.LegacyAliases), role => role.Contains('-'));
    }

    // SECTION: U10, plain words

    [Theory]
    [InlineData("Areas/Admin/Pages/Documents/Recycle.cshtml")]
    [InlineData("Areas/Admin/Pages/Projects/Trash.cshtml")]
    [InlineData("Areas/Admin/Pages/Calendar/Deleted.cshtml")]
    [InlineData("wwwroot/js/admin/admin-document-recycle.js")]
    [InlineData("wwwroot/js/admin/admin-project-trash.js")]
    [InlineData("wwwroot/js/admin/admin-calendar-recovery.js")]
    [InlineData("Areas/Admin/Pages/Documents/Recycle.cshtml.cs")]
    [InlineData("Areas/Admin/Pages/Projects/Trash.cshtml.cs")]
    public void TheBins_SayDeleteForGood_AndNoOperations(string path)
    {
        var text = Read(path);

        foreach (var phrase in new[]
                 {
                     "Delete permanently", "Recovery operation", "records per operation", "Controlled destruction",
                     "Controlled permanent deletion", "Review the operation", "measured storage", "Calendar recovery",
                     "recovery operation", "in one operation", "metadata", "Retention state", "Due for purge"
                 })
        {
            Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheBins_ShowTheBulkLimitPlainly()
    {
        Assert.Contains("<small>Up to @Model.MaximumBulkDocuments at a time.</small>", Read("Areas/Admin/Pages/Documents/Recycle.cshtml"));
        Assert.Contains("Delete for good", Read("wwwroot/js/admin/admin-document-recycle.js"));
        Assert.Contains("Delete project for good", Read("wwwroot/js/admin/admin-project-trash.js"));
    }

    [Theory]
    [InlineData("Areas/Admin/Pages/Users/Disable.cshtml.cs", new[] { "Block sign-in access", "preserving records", "Reason for disabling access", "Username confirmation", "operational impact" })]
    [InlineData("Areas/Admin/Pages/Users/Create.cshtml", new[] { "Permanent sign-in name", "Official rank", "approved way" })]
    [InlineData("Areas/Admin/Pages/Users/Create.cshtml.cs", new[] { "minimum required access", "issue a temporary password" })]
    [InlineData("Areas/DocumentRepository/Pages/Admin/RestoredHidden/Index.cshtml", new[] { "Made inactive on purpose", "Reactivate all", ">Reactivate<" })]
    [InlineData("Areas/DocumentRepository/Pages/Admin/RestoredHidden/Index.cshtml.cs", new[] { "before restore was fixed", "reactivated" })]
    [InlineData("Pages/Shared/Components/RestoredHiddenNotice/Default.cshtml", new[] { "before restore was fixed" })]
    [InlineData("Pages/Usage/Index.cshtml", new[] { "Command monitoring", "Adoption", "authenticated navigation", "heartbeat", "Operational action", "Administrative action", "Monitored from", "Monitoring started", "Usage heatmap", "Approx. active time" })]
    [InlineData("Services/Usage/ErpUsageQueryService.cs", new[] { "Historical audited operational", "Historical audited administrative", "Comprehensive usage monitoring", "Interactive ERP use", "\"ERP navigation\"", "navigation monitoring" })]
    [InlineData("Pages/Celebrations/Index.cshtml", new[] { "Calendar record, not user profile", "independent annual calendar entries", "User-account profile", "Active calendar records", "Upcoming occurrences", "current filters" })]
    [InlineData("Pages/Celebrations/Edit.cshtml", new[] { "Master data", "Maintain an annual calendar record", "user account profile", "Permission scope", "historical context", "recurs annually" })]
    [InlineData("Pages/Celebrations/Edit.cshtml.cs", new[] { "can maintain" })]
    public void FormalWording_IsGone(string path, string[] phrases)
    {
        var text = Read(path);

        foreach (var phrase in phrases)
        {
            Assert.DoesNotContain(phrase, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRoleCapabilityList_IsPlain()
    {
        var items = new AdminRoleAccessCatalog().BuildForRoles(RoleNames.AssignableRoles.Concat(RoleNames.LegacyAliases)).SelectMany(group => group.Items).ToArray();
        Assert.NotEmpty(items);
        var formal = new Regex(
            @"\b(govern|maintain|authoris|authoriz|operational|taxonom|purge|perform|provision|master data|soft-delete|configuration|override|command-level|subject to|record-level|deliberately|inline-edit)",
            RegexOptions.IgnoreCase);

        foreach (var item in items)
        {
            Assert.False(formal.IsMatch(item.Title), $"Formal title: {item.Title}");
            Assert.False(formal.IsMatch(item.Description), $"Formal description: {item.Description}");
        }
    }

    [Fact]
    public void ErpUsage_HeatmapCellsSayWhatHappened()
    {
        var service = Read("Services/Usage/ErpUsageQueryService.cs");

        Assert.Contains("CountedChanges(operationalCount, \"change\"", service);
        Assert.Contains("CountedChanges(administrativeCount, \"admin change\"", service);
        Assert.Contains("\"Not counted yet\"", service);
    }

    // SECTION: U11, figures and their words

    [Fact]
    public void SignInActivity_CaptionsEachTileWithItsOwnFigure()
    {
        var page = Read("Areas/Admin/Pages/Analytics/Logins.cshtml.cs");
        var failuresTile = Regex.Match(page, @"Label = ""Accounts with failures"",[\s\S]*?Detail = (?<detail>[^\n]+)");
        var successTile = Regex.Match(page, @"Label = ""Successful sign-ins"",[\s\S]*?Detail = (?<detail>[^\n]+)");

        Assert.True(failuresTile.Success);
        Assert.Equal("\"Accounts with a failed sign-in or a lockout\",", failuresTile.Groups["detail"].Value.Trim());
        Assert.Contains("Snapshot.UniqueUsers", successTile.Groups["detail"].Value);
        Assert.Contains("Snapshot.UniqueSourceIps", successTile.Groups["detail"].Value);
    }

    [Fact]
    public void Holidays_FilterWordsMatchTheColumns()
    {
        var view = Read("Pages/Settings/Holidays/Index.cshtml");

        Assert.Contains(">Gazetted Holiday</option>", view);
        Assert.Contains(">Restricted Holiday</option>", view);
        Assert.Contains("<option value=\"closed\">Office holiday</option>", view);
        Assert.Contains("<option value=\"informational\">Office open</option>", view);
        Assert.DoesNotContain("Office closed</option>", view);
        Assert.DoesNotContain("Information only", view);
        // The query values stay, so old links keep working.
        Assert.Contains("value=\"closed\"", view);
        Assert.Contains("value=\"informational\"", view);
    }

    [Fact]
    public void SystemHealth_NamesOnlyWhatTheFiguresShow()
    {
        var warningOnly = Health(AdminHealthStatus.Warning, AdminHealthStatus.Healthy);
        Assert.Equal("1 check has a warning. Each row below says what it found.", DbHealthModel.OverviewSentence(warningOnly));
        Assert.DoesNotContain("not run yet", DbHealthModel.OverviewSentence(warningOnly));

        var notRun = Health(AdminHealthStatus.Unavailable, AdminHealthStatus.Unavailable, AdminHealthStatus.Healthy);
        Assert.Equal("2 checks have not run yet. Each row below says what it found.", DbHealthModel.OverviewSentence(notRun));

        var both = Health(AdminHealthStatus.Warning, AdminHealthStatus.Unavailable);
        Assert.Equal("1 check has a warning and 1 check has not run yet. Each row below says what it found.", DbHealthModel.OverviewSentence(both));

        Assert.Equal("1 check failed. Its row below says what to do.", DbHealthModel.OverviewSentence(Health(AdminHealthStatus.Critical, AdminHealthStatus.Unavailable)));
        Assert.Equal("Everything that was checked is working.", DbHealthModel.OverviewSentence(Health(AdminHealthStatus.Healthy)));

        Assert.DoesNotContain("have warnings or have not run yet", Read("Areas/Admin/Pages/Diagnostics/DbHealth.cshtml"));
    }

    // SECTION: Helpers

    private static AdminSystemHealthSnapshot Health(params AdminHealthStatus[] statuses) => new(
        statuses.Max(),
        DateTimeOffset.UnixEpoch,
        "Test",
        "1.0",
        statuses.Select((status, index) => new AdminSystemHealthCheck(
            $"check-{index}", "Group", $"Check {index}", status, "Summary", null, null, 0, DateTimeOffset.UnixEpoch)).ToArray(),
        null!);

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminUserQueryService UserQueries(ApplicationDbContext db) => new(
        db,
        new UserAccountStateResolver(),
        new AdminTimeService(new FixedClock(new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero))),
        Options.Create(new UserLifecycleOptions { HardDeleteWindowHours = 24 }));

    private static IAdminTimeService Time() =>
        new AdminTimeService(new FixedClock(new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero)));

    private static IdentityRole Role(string id, string name) => new(name) { Id = id, NormalizedName = name.ToUpperInvariant() };

    private static ApplicationUser User(string id, string fullName, string userName) => new()
    {
        Id = id,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        FullName = fullName,
        Rank = "Test",
        CreatedUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
