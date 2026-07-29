using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppReferenceDataServiceTests
{
    [Fact]
    public async Task SaveAsync_AddsControlledValue_AndRejectsNormalizedDuplicate()
    {
        await using var db = CreateContext();
        var audit = new FakeAuditService();
        var service = CreateService(db, audit);

        var added = await service.SaveAsync(new ArppReferenceDataSaveCommand(
            ArppReferenceDataKind.Cfa,
            null,
            "  Comdt   SDD  ",
            null,
            10,
            null,
            "admin-1",
            "Admin One"));

        Assert.True(added.Success);
        var option = await db.ArppCfaOptions.SingleAsync();
        Assert.Equal("Comdt   SDD", option.Name);
        Assert.Equal("COMDT SDD", option.NormalizedName);
        Assert.Contains("MasterData.ArppReferenceAdded", audit.Actions);

        var duplicate = await service.SaveAsync(new ArppReferenceDataSaveCommand(
            ArppReferenceDataKind.Cfa,
            null,
            "comdt sdd",
            null,
            20,
            null,
            "admin-1",
            "Admin One"));

        Assert.False(duplicate.Success);
        Assert.Single(await db.ArppCfaOptions.ToListAsync());
    }

    [Fact]
    public async Task EditingMasterValue_DoesNotRewriteHistoricalEntrySnapshot()
    {
        await using var db = CreateContext();
        var option = new ArppCfaOption
        {
            Name = "Comdt SDD",
            NormalizedName = "COMDT SDD",
            SortOrder = 0,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };
        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
            PppNumber = "ARPP/IR&D/N/2026-27/1",
            ProjectReference = "Project One",
            Category = ArppCategory.New,
            IpaCost = 1_000_000m,
            CfaOption = option,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = CreateService(db, new FakeAuditService());
        var result = await service.SaveAsync(new ArppReferenceDataSaveCommand(
            ArppReferenceDataKind.Cfa,
            option.Id,
            "Commandant SDD",
            null,
            0,
            Convert.ToBase64String(option.RowVersion),
            "admin-1",
            "Admin One"));

        Assert.True(result.Success);
        Assert.Equal("Commandant SDD", (await db.ArppCfaOptions.SingleAsync()).Name);
        Assert.Equal("Comdt SDD", (await db.ArppEntries.SingleAsync()).Cfa);
    }

    [Fact]
    public async Task DeactivatedValue_RemainsAvailableForRowThatAlreadyUsesIt()
    {
        await using var db = CreateContext();
        var option = new ArppFundOption
        {
            Name = "IR&D",
            NormalizedName = "IR&D",
            SortOrder = 0,
            IsActive = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };
        db.ArppFundOptions.Add(option);
        await db.SaveChangesAsync();

        var service = CreateService(db, new FakeAuditService());
        var withoutSelection = await service.GetWorkspaceOptionsAsync([], [], []);
        var withSelection = await service.GetWorkspaceOptionsAsync([], [option.Id], []);

        Assert.Empty(withoutSelection.FundOptions);
        Assert.Single(withSelection.FundOptions);
        Assert.False(withSelection.FundOptions[0].IsActive);
    }

    private static ArppReferenceDataService CreateService(ApplicationDbContext db, IAuditService audit)
        => new(db, new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)), audit);

    private static ArppIssue CreateIssue()
        => new()
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP 2026-27",
            IssueDate = new DateOnly(2026, 4, 10),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeAuditService : IAuditService
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }
}
