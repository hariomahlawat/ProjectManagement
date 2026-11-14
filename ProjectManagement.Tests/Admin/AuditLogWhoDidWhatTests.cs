using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Tests.Fakes;
using Xunit;
using SearchIndexModel = ProjectManagement.Areas.Admin.Pages.Diagnostics.SearchIndexModel;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Admin fix PR 4: the audit log names the person behind every event, gives events readable
/// titles and records Search index maintenance.
/// </summary>
public sealed class AuditLogWhoDidWhatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Actor_IsThePersonsFullName_HoweverTheRowRecordedThem()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "hariomahlawat",
            NormalizedUserName = "HARIOMAHLAWAT",
            FullName = "Hari Om Ahlawat"
        });
        db.Users.Add(new ApplicationUser
        {
            Id = "user-2",
            UserName = "nofullname",
            NormalizedUserName = "NOFULLNAME",
            FullName = string.Empty
        });
        db.AuditLogs.AddRange(
            Log("IdOnly", userId: "user-1"),
            Log("UserNameOnly", userName: "hariomahlawat"),
            Log("IdAndSystemMarker", userId: "user-1", userName: "System"),
            Log("PayloadActor", dataJson: "{\"ActorUserId\":\"user-1\",\"ActorName\":\"hariomahlawat\"}"),
            Log("NoFullName", userId: "user-2"),
            Log("PrismSystem", userName: "PRISM system"),
            Log("Nobody"),
            Log("RemovedAccount", userId: "user-gone"),
            Log("FailedSignIn", userName: "someone-typed-this"));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(new AdminLogQuery(null, null, null, null, null, null, null, null));
        var actors = result.Rows.ToDictionary(row => row.Action, row => row.Actor);

        Assert.Equal("Hari Om Ahlawat", actors["IdOnly"]);
        Assert.Equal("Hari Om Ahlawat", actors["UserNameOnly"]);
        Assert.Equal("Hari Om Ahlawat", actors["IdAndSystemMarker"]);
        Assert.Equal("Hari Om Ahlawat", actors["PayloadActor"]);
        Assert.Equal("nofullname", actors["NoFullName"]);
        Assert.Equal(AuditActorNames.SystemActor, actors["PrismSystem"]);
        Assert.Equal(AuditActorNames.SystemActor, actors["Nobody"]);
        Assert.Equal(AuditActorNames.UnknownPerson, actors["RemovedAccount"]);
        Assert.Equal("someone-typed-this", actors["FailedSignIn"]);
        Assert.Equal("PRISM", AuditActorNames.SystemActor);
    }

    [Fact]
    public async Task ActorFilter_FindsRowsThatKeptOnlyTheUserIdOrUserName_ByFullName()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "hariomahlawat",
            NormalizedUserName = "HARIOMAHLAWAT",
            FullName = "Hari Om Ahlawat"
        });
        db.AuditLogs.AddRange(
            Log("Mine", userId: "user-1"),
            Log("MyFailedSignIn", userName: "HariOmAhlawat"),
            Log("Someone else", userId: "user-9", userName: "other"));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(
            new AdminLogQuery(null, null, "Om Ahl", null, null, null, null, null));

        Assert.Equal(
            new[] { "Mine", "MyFailedSignIn" },
            result.Rows.Select(row => row.Action).OrderBy(action => action).ToArray());
    }

    [Fact]
    public async Task ActorsSummary_CountsEachPersonOnce_AndNotPrism()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "hariomahlawat",
            NormalizedUserName = "HARIOMAHLAWAT",
            FullName = "Hari Om Ahlawat"
        });
        db.AuditLogs.AddRange(
            Log("A", userId: "user-1", userName: "hariomahlawat"),
            Log("FailedSignInFirst", userName: "hariomahlawat"),
            Log("B", userId: "user-1"),
            Log("C", userId: "user-2", userName: "other"),
            Log("D", userName: "PRISM system"),
            Log("E", userName: "System"));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(new AdminLogQuery(null, null, null, null, null, null, null, null));

        Assert.Equal(2, result.Summary!.Actors);
    }

    [Theory]
    [InlineData("Arpp.IpaStageSomethingNew", "ARPP IPA stage something new")]
    [InlineData("ProjectBriefing.PowerPointExported", "Project briefing PowerPoint exported")]
    [InlineData("ProjectBriefing.DeckArchived", "Project briefing deck archived")]
    [InlineData("ProjectOfficeReports.FFC.RecordCreated", "FFC record created")]
    [InlineData("ProjectTot.RequestDecided", "Project ToT request decided")]
    [InlineData("Ipr.FilingPdfUploaded", "IPR filing PDF uploaded")]
    [InlineData("DocRepo.Upload", "Document repository upload")]
    [InlineData("CustomAuditAction", "Custom audit action")]
    [InlineData("Total.Recalculated", "Total recalculated")]
    [InlineData("", AuditActionTitleFormatter.DefaultTitle)]
    [InlineData(null, AuditActionTitleFormatter.DefaultTitle)]
    public void FallbackTitle_IsSentenceCaseWithShortFormsKept(string? action, string expected)
    {
        Assert.Equal(expected, AuditActionTitleFormatter.Format(action));
    }

    [Theory]
    [InlineData("ProjectBriefing.DeckRestored", "Briefing deck restored")]
    [InlineData("ProjectBriefing.PowerPointGenerated", "Briefing deck downloaded as PowerPoint")]
    [InlineData("Arpp.IpaStageStartupReconciled", "IPA stages brought in line with ARPP at start-up")]
    [InlineData("SearchIndex.RebuildRequested", "Search index rebuild requested")]
    [InlineData("SearchIndex.RetryRequested", "Failed search indexing retried")]
    public void Catalogue_NamesTheCommonActions(string action, string expected)
    {
        Assert.Equal(expected, new AuditActionPresentationCatalog().Describe(action).Label);
    }

    [Fact]
    public async Task SearchIndexRebuild_IsAudited()
    {
        var (page, audit, store) = CreateSearchIndexPage();

        var result = await page.OnPostRebuildAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains(nameof(ISearchIndexStore.RequestFullRebuildAsync), store.Calls);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SearchIndex.RebuildRequested", entry.Action);
        Assert.Equal("SearchIndex", entry.EntityType);
    }

    [Fact]
    public async Task SearchIndexRetryAll_IsAudited()
    {
        var (page, audit, _) = CreateSearchIndexPage();

        await page.OnPostRetryAllAsync(CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SearchIndex.RetryRequested", entry.Action);
    }

    [Fact]
    public async Task SearchIndexRetryOne_IsAuditedAgainstThatJob()
    {
        var (page, audit, _) = CreateSearchIndexPage();

        await page.OnPostRetryAsync(42, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SearchIndex.RetryRequested", entry.Action);
        Assert.Equal("SearchIndexWorkItem", entry.EntityType);
        Assert.Equal("42", entry.EntityId);
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static AdminLogQueryService CreateService(ApplicationDbContext db) =>
        new(db, new AdminTimeService(FakeClock.AtUtc(Now)));

    private static AuditLog Log(string action, string? userId = null, string? userName = null, string? dataJson = null) =>
        new()
        {
            TimeUtc = Now.AddHours(-1).UtcDateTime,
            Level = "Info",
            Action = action,
            UserId = userId,
            UserName = userName,
            DataJson = dataJson
        };

    private sealed class NoSearchUsage : ProjectManagement.Services.SearchV2.Analytics.ISearchUsageReader
    {
        public Task<ProjectManagement.Services.SearchV2.Analytics.SearchUsageSummary> ReadAsync(TimeSpan window, CancellationToken cancellationToken) =>
            Task.FromResult(ProjectManagement.Services.SearchV2.Analytics.SearchUsageSummary.Empty);
    }

    private static (SearchIndexModel Page, RecordingAudit Audit, RecordingStoreProxy Store) CreateSearchIndexPage()
    {
        var store = DispatchProxy.Create<ISearchIndexStore, RecordingStoreProxy>();
        var audit = new RecordingAudit();
        var page = new SearchIndexModel(
            store,
            new AllowAll(),
            DispatchProxy.Create<ISearchV2Engine, RecordingStoreProxy>(),
            Options.Create(new SearchV2Options()),
            audit,
            new NoSearchUsage())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "admin-1"), new Claim(ClaimTypes.Name, "admin") },
                        "Test"))
                }
            }
        };
        return (page, audit, (RecordingStoreProxy)(object)store);
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class AllowAll : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    public class RecordingStoreProxy : DispatchProxy
    {
        public List<string> Calls { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod?.Name ?? string.Empty);
            if (targetMethod?.ReturnType == typeof(Task)) return Task.CompletedTask;
            throw new InvalidOperationException($"{targetMethod?.Name} is not used by these tests.");
        }
    }
}
