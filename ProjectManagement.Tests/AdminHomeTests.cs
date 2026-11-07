using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.Admin.Pages;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;
using ProjectManagement.Services.Admin.Maintenance;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

// Administration Home (docs/ux/admin-module.md, PR 4): amber or red only when an administrator
// has to act; tiles and figures only for pages the viewer can open.
public sealed class AdminHomeTests
{
    private static readonly DateTimeOffset Checked = new(2026, 11, 6, 8, 35, 0, TimeSpan.Zero);
    private static string When(DateTimeOffset utc) => utc.ToString("HH:mm");

    [Fact]
    public void Status_IsCalm_WhenChecksPassedAndNothingNeedsAnAdministrator()
    {
        var status = AdminHomeRules.ResolveStatus(Array.Empty<AdminHomeLookAt>(), Health(AdminHealthStatus.Healthy), true, When);

        Assert.Equal(AdminHomeTone.Calm, status.Tone);
        Assert.Equal("Running normally", status.Label);
        Assert.Equal("All system checks passed at 08:35. Nothing needs you right now.", status.Summary);
    }

    [Fact]
    public void Status_SaysNotChecked_InsteadOfClaimingAllIsWell_WhenChecksHaveNotRun()
    {
        var status = AdminHomeRules.ResolveStatus(Array.Empty<AdminHomeLookAt>(), null, true, When);

        Assert.Equal(AdminHomeTone.Unknown, status.Tone);
        Assert.Equal("Not checked yet", status.Label);
        Assert.Equal("The system checks have not run since PRISM last started. Open System health to run them.", status.Summary);
        Assert.DoesNotContain("Nothing needs you", status.Summary);
    }

    [Fact]
    public void Status_BeforeTheChecksRun_StillCountsOtherThingsToLookAt()
    {
        var lookAt = AdminHomeRules.BuildLookAt(
            AdminDashboardService.BuildAttention(new AdminDashboardMetrics { LockedUsers = 2 }), null, null);

        var status = AdminHomeRules.ResolveStatus(lookAt, null, true, When);

        Assert.Equal(AdminHomeTone.Attention, status.Tone);
        Assert.Equal("Not checked yet", status.Label);
        Assert.Equal("The system checks have not run since PRISM last started. Open System health to run them. 1 thing to look at below.", status.Summary);
    }

    [Theory]
    [InlineData("document-repository-files", AdminNavigationKeys.MissingDocumentFiles, "Open Missing files")]
    [InlineData("media-availability", AdminNavigationKeys.MediaSources, "Open Media sources")]
    [InlineData("media-face-detector", AdminNavigationKeys.MediaIntelligence, "Open Media intelligence")]
    [InlineData("chat-upload-space", AdminNavigationKeys.ChatStorage, "Open Chat file storage")]
    [InlineData("search-index", AdminNavigationKeys.SearchIndex, "Open Search index")]
    [InlineData("upload-root", AdminNavigationKeys.DatabaseHealth, "Open System health")]
    public void SystemProblem_OpensThePageThatFixesIt(string checkKey, string expectedKey, string expectedLink)
    {
        var item = Assert.Single(AdminHomeRules.BuildLookAt(Array.Empty<AdminDashboardAttention>(), HealthWith(checkKey), null, _ => true));

        Assert.Equal(expectedKey, item.NavigationKey);
        Assert.Equal(expectedLink, item.LinkText);
    }

    [Fact]
    public void SystemProblem_OpensSystemHealth_WhenTheViewerCannotOpenThePageThatFixesIt()
    {
        var item = Assert.Single(AdminHomeRules.BuildLookAt(
            Array.Empty<AdminDashboardAttention>(),
            HealthWith("document-repository-files"),
            null,
            key => key == AdminNavigationKeys.DatabaseHealth));

        Assert.Equal(AdminNavigationKeys.DatabaseHealth, item.NavigationKey);
        Assert.Equal("Open System health", item.LinkText);
    }

    [Fact]
    public void EveryFixPage_IsInTheCatalogue()
    {
        Assert.All(AdminHomeRules.HealthCheckFixPages.Values, key => Assert.NotNull(AdminNavigationCatalog.GetEntry(key)));
    }

    [Fact]
    public async Task Service_LinksASystemProblemToItsPage_OnlyWhenTheViewerCanOpenIt()
    {
        var withPage = CreateService(
            new LatestOnlyHealth(HealthWith("media-availability")),
            new AdminDashboardMetrics(),
            AdminNavigationKeys.DatabaseHealth,
            AdminNavigationKeys.MediaSources);
        var withoutPage = CreateService(
            new LatestOnlyHealth(HealthWith("media-availability")),
            new AdminDashboardMetrics(),
            AdminNavigationKeys.DatabaseHealth);

        var linked = Assert.Single((await withPage.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()))).LookAt);
        var fallback = Assert.Single((await withoutPage.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()))).LookAt);

        Assert.Equal(AdminNavigationKeys.MediaSources, linked.NavigationKey);
        Assert.Equal(AdminNavigationKeys.DatabaseHealth, fallback.NavigationKey);
    }

    private static AdminSystemHealthSnapshot HealthWith(string checkKey) =>
        new(AdminHealthStatus.Warning, Checked, "Production", "1.0",
            new[] { new AdminSystemHealthCheck(checkKey, "Storage", "A check", AdminHealthStatus.Warning, "Something needs a look.", null, null, 1, Checked) },
            new DatabaseHealthSnapshot(true, null, null, null, null, null, null, "(test)", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<DatabaseHealthCheck>()));

    [Fact]
    public void Status_IsRed_OnlyWhenASystemCheckFailed()
    {
        var health = Health(AdminHealthStatus.Critical);
        var lookAt = AdminHomeRules.BuildLookAt(Array.Empty<AdminDashboardAttention>(), health, null);

        var status = AdminHomeRules.ResolveStatus(lookAt, health, true, When);

        Assert.Equal(AdminHomeTone.Urgent, status.Tone);
        Assert.Equal("Needs attention", status.Label);
        var item = Assert.Single(lookAt);
        Assert.True(item.Urgent);
        Assert.Equal("Not writable.", item.Title);
        Assert.Equal("Document repository storage", item.Detail);
        Assert.Equal(AdminNavigationKeys.DatabaseHealth, item.NavigationKey);
    }

    [Fact]
    public void EachSystemProblem_GetsItsOwnLine_FailedFirst_AndNotRunYetIsLeftOut()
    {
        var checks = new List<AdminSystemHealthCheck>
        {
            new("files", "Storage", "Document Repository files", AdminHealthStatus.Warning, "110 documents have lost their file.", null, "Open Missing files.", 1, Checked),
            new("worker", "Background services", "Audit retention", AdminHealthStatus.Unavailable, "Starting.", null, null, -1, Checked),
            new("chat", "Storage", "Space for chat files", AdminHealthStatus.Critical, "Chat file uploads have stopped.", null, null, -1, Checked)
        };
        for (var index = 0; index < 5; index++)
        {
            checks.Add(new($"extra-{index}", "Storage", $"Extra {index}", AdminHealthStatus.Warning, $"Extra problem {index}.", null, null, 1, Checked));
        }
        var health = new AdminSystemHealthSnapshot(AdminHealthStatus.Critical, Checked, "Production", "1.0", checks,
            new DatabaseHealthSnapshot(false, null, null, null, null, null, null, "(not available)", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<DatabaseHealthCheck>()));

        var lookAt = AdminHomeRules.BuildLookAt(Array.Empty<AdminDashboardAttention>(), health, null);

        // Five lines (the failed check first), then one line for the rest; the not-run worker is absent.
        Assert.Equal(AdminHomeRules.MaxHealthLines + 1, lookAt.Count);
        Assert.Equal("Chat file uploads have stopped.", lookAt[0].Title);
        Assert.True(lookAt[0].Urgent);
        Assert.Equal("110 documents have lost their file.", lookAt[1].Title);
        Assert.Equal("Document Repository files. Open Missing files.", lookAt[1].Detail);
        Assert.Equal("2 more system checks need a look", lookAt[^1].Title);
        Assert.DoesNotContain(lookAt, item => item.Title == "Starting.");
    }

    [Fact]
    public void Status_IsAmber_WhenThereIsSomethingToLookAt()
    {
        var lookAt = AdminHomeRules.BuildLookAt(
            AdminDashboardService.BuildAttention(new AdminDashboardMetrics { LockedUsers = 2 }),
            Health(AdminHealthStatus.Healthy),
            null);

        var status = AdminHomeRules.ResolveStatus(lookAt, Health(AdminHealthStatus.Healthy), true, When);

        Assert.Equal(AdminHomeTone.Attention, status.Tone);
        Assert.Equal("Running normally", status.Label);
        Assert.EndsWith("1 thing to look at below.", status.Summary);
        Assert.Equal("2 accounts are locked after failed sign-ins", Assert.Single(lookAt).Title);
    }

    [Fact]
    public void NormalStates_AreNotListedToLookAt()
    {
        // A pending password change, a disabled account and full bins are everyday states.
        var metrics = new AdminDashboardMetrics
        {
            MustChangePasswordUsers = 4,
            DisabledUsers = 3,
            TrashedProjects = 5,
            DeletedDocuments = 7,
            DeletedEvents = 2,
            WarningEventsLast24Hours = 9,
            FailedLoginsLast7Days = 12
        };

        var lookAt = AdminHomeRules.BuildLookAt(AdminDashboardService.BuildAttention(metrics), Health(AdminHealthStatus.Healthy), Integrity(MasterDataIntegrityStatus.Warning));

        Assert.Empty(lookAt);
        Assert.Equal(AdminHomeTone.Calm, AdminHomeRules.ResolveStatus(lookAt, Health(AdminHealthStatus.Healthy), true, When).Tone);
    }

    [Fact]
    public void CriticalDataCheckFindings_AreListed_WarningsAreNot()
    {
        var critical = AdminHomeRules.BuildLookAt(Array.Empty<AdminDashboardAttention>(), null, Integrity(MasterDataIntegrityStatus.Critical, findings: 2));

        var item = Assert.Single(critical);
        Assert.Equal("2 problems in the reference lists", item.Title);
        Assert.Equal(AdminNavigationKeys.MasterDataIntegrity, item.NavigationKey);
        Assert.Empty(AdminHomeRules.BuildLookAt(Array.Empty<AdminDashboardAttention>(), null, Integrity(MasterDataIntegrityStatus.Warning)));
    }

    [Fact]
    public void Tiles_AreTheSixTasks_EachWithOneFigure()
    {
        var metrics = new AdminDashboardMetrics
        {
            TotalUsers = 48,
            LockedUsers = 2,
            LoginsLast7Days = 312,
            FailedLoginsLast7Days = 14
        };
        var lists = new MasterDataOverviewSnapshot(
            Enumerable.Range(1, 11).Select(index => Domain(index)).ToArray(),
            Array.Empty<MasterDataRecentChange>(),
            TotalRecords: 230,
            ActiveRecords: 214,
            InactiveRecords: 16,
            RecordsInUse: 180);
        var jobs = new AdminMaintenanceSummary(false, PdfRun(), 0, 0, null, null);

        var tiles = AdminHomeRules.BuildTiles(metrics, 9, Health(AdminHealthStatus.Healthy), lists, jobs, When);

        Assert.Equal(
            new[]
            {
                AdminNavigationKeys.Users,
                AdminNavigationKeys.Logins,
                AdminNavigationKeys.RecoveryCentre,
                AdminNavigationKeys.MasterDataCentre,
                AdminNavigationKeys.DatabaseHealth,
                AdminNavigationKeys.MaintenanceCentre
            },
            tiles.Select(tile => tile.NavigationKey).ToArray());
        Assert.Equal(
            new[]
            {
                "48 accounts · 2 locked",
                "312 sign-ins in 7 days · 14 failed",
                "9 deleted items can be restored",
                "11 lists · 214 active values",
                "All checks passed · 08:35",
                "Last PDF run: completed · 08:35"
            },
            tiles.Select(tile => tile.Fact).ToArray());
    }

    [Fact]
    public void Tiles_ShowNoNumber_WhenAFigureCouldNotBeRead()
    {
        var tiles = AdminHomeRules.BuildTiles(new AdminDashboardMetrics(), 0, null, null, null, When)
            .ToDictionary(tile => tile.NavigationKey);

        Assert.Equal(AdminHomeRules.NoFigure, tiles[AdminNavigationKeys.MasterDataCentre].Fact);
        Assert.Equal(AdminHomeRules.NoFigure, tiles[AdminNavigationKeys.MaintenanceCentre].Fact);
        Assert.Equal("Not checked since PRISM started", tiles[AdminNavigationKeys.DatabaseHealth].Fact);
        Assert.Equal("Nothing deleted to restore", tiles[AdminNavigationKeys.RecoveryCentre].Fact);
    }

    [Fact]
    public async Task Service_ShowsOnlyWhatTheViewerCanOpen_AndNeverRunsTheSystemChecks()
    {
        var health = new LatestOnlyHealth(Health(AdminHealthStatus.Critical));
        var service = CreateService(
            health,
            new AdminDashboardMetrics { LockedUsers = 1, TrashedProjects = 3 },
            AdminNavigationKeys.Dashboard,
            AdminNavigationKeys.RecoveryCentre);

        var snapshot = await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        // Users and System health are not openable: no tile, no "To look at" item, no health status.
        Assert.Equal(AdminNavigationKeys.RecoveryCentre, Assert.Single(snapshot.Tiles).NavigationKey);
        Assert.Empty(snapshot.LookAt);
        Assert.Equal(AdminHomeTone.Calm, snapshot.Status.Tone);
        Assert.Equal(0, health.CheckRuns);
    }

    [Fact]
    public async Task Service_RestoreTileAddsUpTheTabsOfRestoreDeletedItems()
    {
        // Projects 2, project documents 3, repository documents 4, chat files 5; the overview tab
        // has no count and a count that could not be read adds nothing.
        var tabs = new[]
        {
            new AdminRestoreTab(AdminRestoreTabKeys.Overview, "Overview", AdminNavigationKeys.RecoveryCentre, null, false),
            new AdminRestoreTab(AdminRestoreTabKeys.Projects, "Projects", AdminNavigationKeys.ProjectTrash, 2, false),
            new AdminRestoreTab(AdminRestoreTabKeys.ProjectDocuments, "Project documents", AdminNavigationKeys.DocumentRecycle, 3, false),
            new AdminRestoreTab(AdminRestoreTabKeys.RepositoryDocuments, "Repository documents", AdminNavigationKeys.RepositoryTrash, 4, true),
            new AdminRestoreTab(AdminRestoreTabKeys.CalendarEvents, "Calendar events", AdminNavigationKeys.DeletedEvents, null, false),
            new AdminRestoreTab(AdminRestoreTabKeys.ChatFiles, "Chat files", AdminNavigationKeys.ChatStorage, 5, true)
        };
        var service = CreateService(
            new LatestOnlyHealth(null),
            new AdminDashboardMetrics(),
            AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray(),
            failingSources: false,
            restoreTabs: tabs);

        var snapshot = await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(
            "14 deleted items can be restored",
            snapshot.Tiles.Single(tile => tile.NavigationKey == AdminNavigationKeys.RecoveryCentre).Fact);
    }

    [Fact]
    public async Task Service_ReadsTheLastCheckResult_ForAViewerWhoCanOpenSystemHealth()
    {
        var health = new LatestOnlyHealth(Health(AdminHealthStatus.Critical));
        var service = CreateService(
            health,
            new AdminDashboardMetrics(),
            AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray());

        var snapshot = await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(AdminHomeTone.Urgent, snapshot.Status.Tone);
        Assert.True(Assert.Single(snapshot.LookAt).Urgent);
        Assert.Equal(6, snapshot.Tiles.Count);
        Assert.Equal(0, health.CheckRuns);
    }

    [Fact]
    public async Task Service_KeepsHomeWorking_WhenAFigureCannotBeRead()
    {
        var service = CreateService(
            new LatestOnlyHealth(null),
            new AdminDashboardMetrics(),
            AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray(),
            failingSources: true);

        var snapshot = await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(6, snapshot.Tiles.Count);
        Assert.Equal(AdminHomeRules.NoFigure, snapshot.Tiles.Single(tile => tile.NavigationKey == AdminNavigationKeys.MasterDataCentre).Fact);
    }

    [Theory]
    [MemberData(nameof(ExampleWords))]
    public void EveryExampleSearchWord_FindsAPage(string example)
    {
        var words = example.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(AdminNavigationCatalog.Entries, entry =>
        {
            var label = $"{entry.Item.Text} {entry.Keywords} {entry.Description}".ToLowerInvariant();
            return words.All(label.Contains);
        });
    }

    public static IEnumerable<object[]> ExampleWords() =>
        AdminIndexModel.ExampleSearches.Select(example => new object[] { example });

    private static AdminHomeService CreateService(
        IAdminSystemHealthService health,
        AdminDashboardMetrics metrics,
        params string[] openableKeys) =>
        CreateService(health, metrics, openableKeys, failingSources: false);

    private static AdminHomeService CreateService(
        IAdminSystemHealthService health,
        AdminDashboardMetrics metrics,
        string[] openableKeys,
        bool failingSources,
        IReadOnlyList<AdminRestoreTab>? restoreTabs = null) =>
        new(
            new FixedDashboard(metrics),
            health,
            new FixedIntegrity(failingSources),
            new FixedMasterData(failingSources),
            new FixedMaintenance(),
            new FixedAccess(openableKeys),
            new FixedRestoreTabs(restoreTabs ?? new[]
            {
                new AdminRestoreTab(AdminRestoreTabKeys.Projects, "Projects", AdminNavigationKeys.ProjectTrash, metrics.TrashedProjects, false)
            }),
            new AdminTimeService(FakeClock.AtUtc(Checked)),
            NullLogger<AdminHomeService>.Instance);

    private static AdminSystemHealthSnapshot Health(AdminHealthStatus status)
    {
        var checks = new List<AdminSystemHealthCheck>
        {
            Check("upload-root", "Primary upload storage", AdminHealthStatus.Healthy, "Writable.")
        };
        if (status != AdminHealthStatus.Healthy)
        {
            checks.Add(Check("document-repository", "Document repository storage", status, "Not writable."));
        }

        return new AdminSystemHealthSnapshot(
            status,
            Checked,
            "Production",
            "1.0",
            checks,
            new DatabaseHealthSnapshot(false, null, null, null, null, null, null, "(not available)", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<DatabaseHealthCheck>()));
    }

    private static AdminSystemHealthCheck Check(string key, string label, AdminHealthStatus status, string summary) =>
        new(key, "Storage", label, status, summary, null, null, 1, Checked);

    private static MasterDataIntegritySnapshot Integrity(MasterDataIntegrityStatus status, int findings = 1)
    {
        var check = new MasterDataIntegrityCheck(
            "check",
            "Check",
            "Description",
            "bi-check",
            status,
            Enumerable.Range(1, findings).Select(index => new MasterDataIntegrityFinding("code", $"Finding {index}")).ToArray(),
            CanRepair: false);
        return new MasterDataIntegritySnapshot(
            new[] { check },
            Healthy: 0,
            Warnings: status == MasterDataIntegrityStatus.Warning ? 1 : 0,
            Critical: status == MasterDataIntegrityStatus.Critical ? 1 : 0,
            Findings: findings,
            Checked);
    }

    private static MasterDataDomainSummary Domain(int index) =>
        new($"list-{index}", $"List {index}", "", "bi-list", "Admin", "/MasterData/Index", 10, 9, 1, 5, "policy");

    private static PdfIngestionRunRecord PdfRun() =>
        new(Guid.NewGuid(), Checked.AddMinutes(-5), Checked, "admin", "Completed", 10, 8, 2, 0, 0,
            Array.Empty<PdfIngestionSourceSummary>(), Array.Empty<PdfIngestionFailure>(), null);

    private sealed class LatestOnlyHealth(AdminSystemHealthSnapshot? latest) : IAdminSystemHealthService
    {
        public int CheckRuns { get; private set; }

        public Task<AdminSystemHealthSnapshot> CheckAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            CheckRuns++;
            throw new InvalidOperationException("Home must not run the system checks.");
        }

        public AdminSystemHealthSnapshot? GetLatest() => latest;
    }

    private sealed class FixedDashboard(AdminDashboardMetrics metrics) : IAdminDashboardService
    {
        public Task<AdminDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminDashboardSnapshot(metrics, Array.Empty<AdminDashboardAction>(), AdminDashboardService.BuildAttention(metrics)));
    }

    private sealed class FixedIntegrity(bool fail) : IAdminMasterDataIntegrityService
    {
        public Task<MasterDataIntegritySnapshot> InspectAsync(CancellationToken cancellationToken = default) =>
            fail ? throw new InvalidOperationException("Database unavailable.") : Task.FromResult(Integrity(MasterDataIntegrityStatus.Warning));

        public Task<AdminOperationResult> NormaliseOrderAsync(string checkKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedMasterData(bool fail) : IMasterDataAdministrationQueryService
    {
        public Task<MasterDataOverviewSnapshot> GetOverviewAsync(int holidayYear, CancellationToken cancellationToken = default) =>
            fail
                ? throw new InvalidOperationException("Database unavailable.")
                : Task.FromResult(new MasterDataOverviewSnapshot(new[] { Domain(1) }, Array.Empty<MasterDataRecentChange>(), 10, 9, 1, 5));

        public Task<CategoryDirectoryResult> GetCategoriesAsync(MasterDataCategoryKind kind, CategoryDirectoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CategoryImpact?> GetCategoryImpactAsync(MasterDataCategoryKind kind, int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FlatLookupDirectoryResult> GetFlatLookupAsync(MasterDataFlatLookupKind kind, FlatLookupDirectoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityTypeDirectoryResult> GetActivityTypesAsync(ActivityTypeDirectoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedMaintenance : IAdminMaintenanceSummaryService
    {
        public Task<AdminMaintenanceSummary> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminMaintenanceSummary(false, null, 0, 0, null, null));
    }

    private sealed class FixedRestoreTabs(IReadOnlyList<AdminRestoreTab> tabs) : IAdminRestoreTabsService
    {
        public Task<IReadOnlyList<AdminRestoreTab>> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult(tabs);
    }

    private sealed class FixedAccess(string[] keys) : IAdminNavigationAccess
    {
        public Task<bool> CanOpenAsync(ClaimsPrincipal user, AdminNavigationEntry entry) =>
            Task.FromResult(keys.Contains(entry.Key));

        public Task<IReadOnlyList<AdminNavigationEntry>> GetOpenableEntriesAsync(ClaimsPrincipal user) =>
            Task.FromResult<IReadOnlyList<AdminNavigationEntry>>(AdminNavigationCatalog.Entries.Where(entry => keys.Contains(entry.Key)).ToArray());
    }
}
