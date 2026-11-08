using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Pages.Logs;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Fourth review PR 7 (U5, U9): the audit log reads in plain words, names the record each entry
/// changed, uses the sidebar's category names, and shows people by name everywhere.
/// </summary>
public sealed class AdminReadableAuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);

    // ---- Plain details, worked out at display time from what old entries stored ----

    [Theory]
    [InlineData("Arpp.IpaStageSynchronized",
        "Synchronized the IPA stage from the first published ARPP position for project 185.",
        "{\"ProjectId\":\"185\",\"CompletionDate\":\"2025-04-22\",\"AuthorityDocument\":\"Original ARPP\"}",
        "IPA stage set as completed on 22 Apr 2025, from the Original ARPP.")]
    [InlineData("Arpp.IpaStageSynchronized", "x",
        "{\"ProjectId\":\"185\",\"CompletionDate\":\"2025-10-14\",\"AuthorityDocument\":\"Addendum No. 1\"}",
        "IPA stage set as completed on 14 Oct 2025, from Addendum No. 1.")]
    [InlineData("Arpp.IpaStageDataQualityIssue",
        "IPA actual start is later than the ARPP-derived completion date for project 217.",
        "{\"ProjectId\":\"217\",\"ActualStart\":\"2026-05-01\",\"CompletionDate\":\"2025-10-14\"}",
        "The IPA start (1 May 2026) is later than the ARPP completion date (14 Oct 2025).")]
    [InlineData("Arpp.IpaStageStartupReconciled",
        "Reconciled 24 IPA lifecycle stages from published ARPP records.",
        "{\"ChangedProjectCount\":\"24\",\"EvaluatedProjectCount\":\"48\"}",
        "24 of 48 projects brought in line with the published ARPP.")]
    [InlineData("ProjectBriefing.AdditionalSlideEnabled",
        "FfcGlobalFootprint additional slide enabled.",
        "{\"DeckId\":\"1\",\"DeckName\":\"Demo deck\"}",
        "FFC global footprint slide switched on.")]
    [InlineData("ProjectBriefing.AdditionalSlideDisabled",
        "RoleAndCharter additional slide disabled.",
        "{\"DeckId\":\"1\"}",
        "Role and charter slide switched off.")]
    [InlineData("ProjectBriefing.ProjectsAdded",
        "1 project(s) added to briefing deck.",
        "{\"DeckId\":\"1\"}",
        "1 project added.")]
    [InlineData("ProjectBriefing.ProjectsAdded",
        "49 project(s) added to briefing deck.",
        "{\"DeckId\":\"1\"}",
        "49 projects added.")]
    [InlineData("ProjectBriefing.MembershipUpdated",
        "Briefing deck membership updated: 1 added, 0 removed.",
        "{\"DeckId\":\"1\"}",
        "1 project added.")]
    [InlineData("ProjectBriefing.DeckDeleted",
        "Project briefing deck deleted; it can be restored for 30 days.",
        "{\"DeckId\":\"2\"}",
        "It can be restored for 30 days.")]
    [InlineData("ProjectBriefing.PowerPointGenerated",
        "Project briefing PowerPoint generated.",
        "{\"DeckId\":\"1\",\"SlideCount\":\"57\"}",
        "57 slides.")]
    [InlineData("ProjectBriefing.PresetApplied",
        "Project briefing deck set from the Visit or external audience preset.",
        "{\"DeckId\":\"1\"}",
        "Set from the “Visit or external audience” template.")]
    [InlineData("MasterData.IntegrityOrderNormalised",
        "Normalised display order for 5 record(s).",
        "{\"EntityType\":\"MasterDataIntegrity\",\"EntityId\":\"sponsoring-unit-order\"}",
        "Put 5 items back in order.")]
    [InlineData("MasterData.IntegrityOrderNormalised",
        "Normalised display order for 1 record(s).",
        "{\"EntityType\":\"MasterDataIntegrity\",\"EntityId\":\"sponsoring-unit-order\"}",
        "Put 1 item back in order.")]
    [InlineData("LoginFailed", "Invalid credentials for hariomahlawat", null, "Wrong user name or password.")]
    [InlineData("LoginFailed", "Not allowed for hariomahlawat", null, "This account is not allowed to sign in.")]
    [InlineData("LoginLockedOut", "william", null, "Too many wrong passwords, so the account is locked for a while.")]
    [InlineData("Arpp.WorkspaceSaved",
        "Saved ARPP 2024-25 (IR&D) with 10 rows.",
        "{\"IssueId\":\"1\",\"EntryCount\":\"10\",\"LinkedProjectCount\":\"8\"}",
        "Saved with 10 rows, 8 linked to projects.")]
    [InlineData("Arpp.IssueUnlocked",
        "Unlocked ARPP 2025-26 for correction.",
        "{\"IssueId\":\"5\",\"Reason\":\"Wrong serial\"}",
        "Unlocked for correction. Reason: Wrong serial.")]
    [InlineData("AdminExport.Users", "Users exported: 4 rows.", "{\"EntityType\":\"Export\"}", "4 rows exported.")]
    [InlineData("AdminExport.AuditLog", "Audit log exported: 1 rows.", "{\"EntityType\":\"Export\"}", "1 row exported.")]
    [InlineData("AdminUserRolesUpdated", null, "{\"Added\":\"Project Officer\",\"Removed\":\"\"}", "Added Project Officer.")]
    [InlineData("project.video.upload",
        "Video 'Night engagement' uploaded to project 214",
        "{\"projectId\":\"214\"}",
        "Video “Night engagement” added.")]
    public void Describe_GivesAPlainDetail(string action, string? message, string? dataJson, string expected)
    {
        var text = AuditEntryDescriber.Describe(action, message, "user-1", "hariomahlawat", new AdminAuditPayloadParser().Parse(dataJson));

        Assert.Equal(expected, text.Detail);
        Assert.DoesNotContain("(s)", text.Detail);
    }

    [Theory]
    [InlineData("Project.Content.BriefUpdated", "Updated project content for 'UX TEST'.", "{\"ProjectId\":\"214\"}")]
    [InlineData("Project.ProliferationProfileUpdated", "Proliferation details updated for project UX TEST.", "{\"ProjectId\":\"214\"}")]
    [InlineData("PdfIngestionStarted", "External PDF ingestion started.", null)]
    [InlineData("ProjectBriefing.DeckUpdated", "Project briefing deck settings updated.", "{\"DeckId\":\"1\"}")]
    [InlineData("AdminUserPurged", "abhishek", "{\"EntityType\":\"ApplicationUser\",\"EntityId\":\"u-2\"}")]
    public void Describe_DropsDetailsThatOnlyRepeatTheTitleOrAUserName(string action, string message, string? dataJson)
    {
        var text = AuditEntryDescriber.Describe(action, message, null, null, new AdminAuditPayloadParser().Parse(dataJson));

        Assert.Null(text.Detail);
    }

    [Fact]
    public void Describe_UnknownActions_KeepTheirMessage_WithPluralsFixed()
    {
        var text = AuditEntryDescriber.Describe("Something.Else", "Moved 3 file(s) and 1 folder(s).", null, null, null);

        Assert.Equal("Moved 3 files and 1 folder.", text.Detail);
    }

    [Fact]
    public void NewCatalogueTitles_ArePlain()
    {
        var catalog = new AuditActionPresentationCatalog();

        Assert.Equal("IPA stage set from the ARPP", catalog.Describe("Arpp.IpaStageSynchronized").Label);
        Assert.Equal("List put back in order", catalog.Describe("MasterData.IntegrityOrderNormalised").Label);
        Assert.Equal("Extra slide switched on in a briefing deck", catalog.Describe("ProjectBriefing.AdditionalSlideEnabled").Label);
        Assert.Equal("Copying PDFs started", catalog.Describe("PdfIngestionStarted").Label);
        Assert.Equal("Roles changed", catalog.Describe("AdminUserRolesUpdated").Label);
    }

    // ---- Category names match the sidebar; the query values stay ----

    [Fact]
    public void Categories_UseTheSidebarNames()
    {
        var catalog = new AuditActionPresentationCatalog();

        Assert.Equal("Lists used in forms", catalog.Describe("MasterData.ProjectTypeCreated").Category);
        Assert.Equal("Lists used in forms", catalog.Describe("MasterData.SomethingNew").Category);
        Assert.Equal("Copy PDFs into the repository", catalog.Describe("PdfIngestionCompleted").Category);
        Assert.Equal("Legacy project import", catalog.Describe("LegacyImportCommitted").Category);

        Assert.Equal("Lists used in forms", IndexModel.CategoryLabel("master-data"));
        Assert.Equal("Copy PDFs into the repository", IndexModel.CategoryLabel("pdf-ingestion"));
        Assert.Equal("Legacy project import", IndexModel.CategoryLabel("legacy-import"));
        Assert.Equal("Access & security", IndexModel.CategoryLabel("access-security"));
    }

    [Fact]
    public async Task CategoryFilter_KeepsItsQueryValues_AndAcceptsTheListHubsOldLink()
    {
        await using var db = CreateInMemoryDb();
        db.AuditLogs.AddRange(
            Log("MasterData.ProjectTypeCreated"),
            Log("PdfIngestionStarted"),
            Log(AuthenticationEventNames.AuditLoginSuccess, userName: "someone"));
        await db.SaveChangesAsync();
        var service = CreateLogService(db);

        foreach (var value in new[] { "master-data", "master data" })
        {
            var lists = await service.GetAsync(Query() with { Category = value });
            Assert.Equal("MasterData.ProjectTypeCreated", Assert.Single(lists.Rows).Action);
        }

        var pdf = await service.GetAsync(Query() with { Category = "pdf-ingestion" });
        Assert.Equal("PdfIngestionStarted", Assert.Single(pdf.Rows).Action);
    }

    // ---- Affected record filled, by name ----

    [Fact]
    public async Task AffectedRecord_IsFilled_ByName_ForRowsThatNameTheirRecordInTheirDetails()
    {
        await using var db = CreateInMemoryDb();
        db.Users.AddRange(
            Person("user-1", "hariomahlawat", "Hari Om Ahlawat"),
            Person("user-2", "abhishek", "Abhishek Rao"));
        db.Projects.Add(new Project { Id = 185, Name = "Man-portable Counter-UAS System", CreatedByUserId = "user-1" });
        db.ProjectBriefingDecks.Add(new ProjectBriefingDeck { Id = 7, Name = "Visit of the Comdt", NormalizedName = "VISIT OF THE COMDT", OwnerUserId = "user-1" });
        db.ArppIssues.Add(new ArppIssue { Id = 5, Name = "ARPP 2025-26 Addendum 1" });
        db.SponsoringUnits.Add(new SponsoringUnit { Id = 12, Name = "Infantry Directorate" });
        db.AuditLogs.AddRange(
            Log("Arpp.IpaStageSynchronized", dataJson: "{\"ProjectId\":\"185\",\"CompletionDate\":\"2025-04-22\"}"),
            Log("ProjectBriefing.ProjectsAdded", message: "2 project(s) added to briefing deck.", dataJson: "{\"DeckId\":\"7\",\"DeckName\":\"Old deck name\"}"),
            Log("Arpp.IssueVerified", dataJson: "{\"IssueId\":\"5\"}"),
            Log(AuthenticationEventNames.AuditLoginFailed, message: "Invalid credentials for HariOmAhlawat", userName: "HariOmAhlawat"),
            Log(AuthenticationEventNames.AuditLoginFailed, message: "Invalid credentials for nobody", userName: "nobody"),
            Log("AdminUserPasswordReset", message: "abhishek",
                dataJson: "{\"EntityType\":\"ApplicationUser\",\"EntityId\":\"user-2\",\"ActorUserId\":\"user-1\",\"ActorName\":\"hariomahlawat\"}"),
            Log("MasterData.IntegrityOrderNormalised", message: "Normalised display order for 5 record(s).",
                dataJson: "{\"EntityType\":\"MasterDataIntegrity\",\"EntityId\":\"sponsoring-unit-order\",\"ActorUserId\":\"user-1\"}"),
            Log("MasterData.SponsoringUnitUpdated",
                dataJson: "{\"EntityType\":\"SponsoringUnit\",\"EntityId\":\"12\",\"ActorUserId\":\"user-1\"}"),
            Log("Projects.PlanUpdated", dataJson: "{\"ProjectId\":\"999\"}"));
        await db.SaveChangesAsync();

        var rows = (await CreateLogService(db).GetAsync(Query())).Rows;
        AuditRecordDisplay? Record(string action, int skip = 0) =>
            rows.Where(row => row.Action == action).Skip(skip).First().Record;

        Assert.Equal(new AuditRecordDisplay("Man-portable Counter-UAS System", "Project"), Record("Arpp.IpaStageSynchronized"));
        Assert.Equal(new AuditRecordDisplay("Visit of the Comdt", "Briefing deck"), Record("ProjectBriefing.ProjectsAdded"));
        Assert.Equal(new AuditRecordDisplay("ARPP 2025-26 Addendum 1", "ARPP issue"), Record("Arpp.IssueVerified"));
        Assert.Equal(new AuditRecordDisplay("Abhishek Rao", "Person"), Record("AdminUserPasswordReset"));
        Assert.Equal(new AuditRecordDisplay("Sponsoring units", "List"), Record("MasterData.IntegrityOrderNormalised"));
        Assert.Equal(new AuditRecordDisplay("Infantry Directorate", "Sponsoring unit"), Record("MasterData.SponsoringUnitUpdated"));
        Assert.Equal(new AuditRecordDisplay("No. 999", "Project, no longer in PRISM"), Record("Projects.PlanUpdated"));

        // A failed sign-in keeps only what was typed; it names the account when one matches.
        var failed = rows.Where(row => row.Action == AuthenticationEventNames.AuditLoginFailed)
            .Select(row => row.Record)
            .ToArray();
        Assert.Contains(new AuditRecordDisplay("Hari Om Ahlawat", "Person"), failed);
        Assert.Contains(new AuditRecordDisplay("nobody", "User name typed (no such account)"), failed);

        var reset = rows.Single(row => row.Action == "AdminUserPasswordReset");
        Assert.Equal("Hari Om Ahlawat", reset.Actor);
        Assert.DoesNotContain("abhishek", reset.Detail ?? string.Empty);
        Assert.Equal("2 projects added.", rows.Single(row => row.Action == "ProjectBriefing.ProjectsAdded").Detail);
    }

    [Fact]
    public async Task OldAccountEntries_DoNotCallTheAffectedAccountTheActor()
    {
        await using var db = CreateInMemoryDb();
        db.Users.AddRange(Person("admin-1", "hariomahlawat", "Hari Om Ahlawat"), Person("u-sandeep", "sandeep", "Sandeep Kumar"));
        db.AuditLogs.AddRange(
            Log("AdminUserDisabled", userId: "u-sandeep", userName: "sandeep", dataJson: "{\"Reason\":\"posted out\",\"Actor\":\"admin-1\"}"),
            Log("AdminUserEnabled", userId: "u-sandeep", userName: "sandeep"));
        await db.SaveChangesAsync();

        var rows = (await CreateLogService(db).GetAsync(Query())).Rows;
        var disabled = rows.Single(row => row.Action == "AdminUserDisabled");
        var enabled = rows.Single(row => row.Action == "AdminUserEnabled");

        Assert.Equal("Hari Om Ahlawat", disabled.Actor);
        Assert.Equal("Reason: posted out.", disabled.Detail);
        Assert.Equal(new AuditRecordDisplay("Sandeep Kumar", "Person"), disabled.Record);
        Assert.Equal(AuditEntryPresenter.ActorNotRecorded, enabled.Actor);
        Assert.Equal(new AuditRecordDisplay("Sandeep Kumar", "Person"), enabled.Record);
    }

    // ---- People by name in the three places that showed user names (U9) ----

    [Fact]
    public async Task ListHub_RecentChanges_ShowPeopleByName_AndTheItemByName()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(Person("user-1", "hariomahlawat", "Hari Om Ahlawat"));
        db.ProjectTypes.Add(new ProjectType { Id = 3, Name = "Build" });
        db.AuditLogs.Add(Log("MasterData.ProjectTypeUpdated", userId: "user-1", userName: "hariomahlawat",
            dataJson: "{\"EntityType\":\"ProjectType\",\"EntityId\":\"3\",\"ActorUserId\":\"user-1\",\"ActorName\":\"hariomahlawat\"}"));
        await db.SaveChangesAsync();

        var snapshot = await new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog())
            .GetOverviewAsync(2026);

        var change = Assert.Single(snapshot.RecentChanges);
        Assert.Equal("Hari Om Ahlawat", change.Actor);
        Assert.Equal("Build", change.Message);
    }

    [Fact]
    public async Task UserPage_AuditHistory_ShowsWhoActedByName()
    {
        await using var db = CreateInMemoryDb();
        db.Users.AddRange(Person("admin-1", "hariomahlawat", "Hari Om Ahlawat"), Person("u-2", "abhishek", "Abhishek Rao"));
        db.AuditLogs.Add(Log("AdminUserPasswordReset", message: "abhishek",
            userId: "admin-1", userName: "abhishek",
            dataJson: "{\"EntityType\":\"ApplicationUser\",\"EntityId\":\"u-2\",\"ActorUserId\":\"admin-1\",\"ActorName\":\"hariomahlawat\"}"));
        await db.SaveChangesAsync();

        var service = new AdminUserQueryService(
            db,
            new UserAccountStateResolver(),
            new AdminTimeService(FakeClock.AtUtc(Now)),
            Options.Create(new UserLifecycleOptions()));
        var activity = Assert.Single(await service.GetRecentAdministrativeActivityAsync("u-2"));

        Assert.Equal("Hari Om Ahlawat", activity.Actor);
        Assert.NotEqual("abhishek", activity.Message);
    }

    [Fact]
    public async Task SignInActivity_ReviewRow_NamesThePerson_ForAFailedSignInThatKeptOnlyTheUserName()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(Person("user-1", "hariomahlawat", "Hari Om Ahlawat"));
        db.AuditLogs.AddRange(
            new AuditLog { TimeUtc = Now.AddHours(-2).UtcDateTime, Level = "Warning", Action = AuthenticationEventNames.AuditLoginLockedOut, UserName = "hariomahlawat" },
            new AuditLog { TimeUtc = Now.AddHours(-3).UtcDateTime, Level = "Warning", Action = AuthenticationEventNames.AuditLoginLockedOut, UserName = "nobody" });
        await db.SaveChangesAsync();

        var snapshot = await new AdminLoginMonitoringService(
                db,
                new AdminTimeService(FakeClock.AtUtc(Now)),
                Options.Create(new AdminLoginMonitoringOptions()))
            .GetAsync(new AdminLoginMonitoringRequest(Days: 7, ReviewOnly: true));

        var known = Assert.Single(snapshot.ReviewRows, row => row.LoginName == "hariomahlawat");
        Assert.Equal("Hari Om Ahlawat", known.DisplayName);
        var unknown = Assert.Single(snapshot.ReviewRows, row => row.LoginName == "nobody");
        Assert.Equal("nobody", unknown.DisplayName);
    }

    // ---- Names are looked up once per page, not once per row ----

    [Fact]
    public async Task Names_AreLookedUpOncePerPage_NotOncePerRow()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var reads = new TableReads();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(reads)
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            for (var index = 1; index <= 25; index++)
            {
                setup.Users.Add(Person($"user-{index}", $"person{index}", $"Person {index}"));
            }

            await setup.SaveChangesAsync();
            for (var index = 1; index <= 25; index++)
            {
                setup.Projects.Add(new Project { Id = index, Name = $"Project {index}", CreatedByUserId = "user-1" });
                setup.AuditLogs.Add(Log("Projects.PlanUpdated", userId: $"user-{index}", dataJson: $"{{\"ProjectId\":\"{index}\"}}",
                    time: Now.AddMinutes(-index)));
            }

            await setup.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        reads.Reset();
        var result = await CreateLogService(db).GetAsync(Query() with { PageSize = 25 });

        Assert.Equal(25, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.StartsWith("Project ", row.Record!.Name));
        Assert.All(result.Rows, row => Assert.StartsWith("Person ", row.Actor));
        Assert.Equal(1, reads.Count("\"Projects\""));
        // People: the actor lookup; the "People" tile reads the users table inside its own count.
        Assert.Equal(1, reads.CountStartingWith("\"AspNetUsers\""));
    }

    private sealed class TableReads : DbCommandInterceptor
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _commands = new();

        public void Reset() => _commands.Clear();

        public int Count(string table) => _commands.Count(text => FromClauseTable(text) == table);

        public int CountStartingWith(string table) => Count(table);

        // The first table a SELECT reads from.
        private static string? FromClauseTable(string text)
        {
            var index = text.IndexOf("FROM ", StringComparison.Ordinal);
            if (index < 0) return null;
            var rest = text[(index + 5)..].TrimStart();
            var end = rest.IndexOfAny(new[] { ' ', '\n', '\r' });
            return end < 0 ? rest : rest[..end];
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                _commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static ApplicationDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static AdminLogQueryService CreateLogService(ApplicationDbContext db) =>
        new(db, new AdminTimeService(FakeClock.AtUtc(Now)));

    private static AdminLogQuery Query() => new(null, null, null, null, null, null, null, null);

    private static ApplicationUser Person(string id, string userName, string fullName) => new()
    {
        Id = id,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        FullName = fullName
    };

    private static AuditLog Log(
        string action,
        string? message = null,
        string? userId = null,
        string? userName = null,
        string? dataJson = null,
        DateTimeOffset? time = null) => new()
    {
        TimeUtc = (time ?? Now.AddHours(-1)).UtcDateTime,
        Level = "Info",
        Action = action,
        Message = message,
        UserId = userId,
        UserName = userName,
        DataJson = dataJson
    };
}
