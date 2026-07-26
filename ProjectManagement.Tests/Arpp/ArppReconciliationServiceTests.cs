using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppReconciliationServiceTests
{
    [Fact]
    public async Task QueueSuggestsProjects_AndConfirmedLinkDoesNotChangeIssuedReference()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "Indigenous Swarm Drones Algorithm",
                CaseFileNumber = "SDD/ALG/01",
                CreatedByUserId = "seed"
            },
            new Project
            {
                Id = 2,
                Name = "Unrelated Simulator",
                CaseFileNumber = "SDD/SIM/02",
                CreatedByUserId = "seed"
            });
        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 8_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var issue = new ArppIssue
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP 2026-27",
            IssueDate = new DateOnly(2026, 2, 26),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "12",
            ProjectReference = "Indigenous swarm drones Algorithm",
            Category = ArppCategory.New,
            IpaCost = 10_000_000m,
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

        var audit = new FakeAuditService();
        var service = new ArppReconciliationService(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)),
            audit);

        var queue = await service.GetQueueAsync(2026, null);
        var item = Assert.Single(queue.Items);
        var suggestion = Assert.Single(item.Suggestions.Where(candidate => candidate.ProjectId == 1));
        Assert.Equal(8_000_000m, suggestion.LegacyIpaCost);

        var result = await service.LinkAsync(new ArppReconciliationCommand(
            [new ArppReconciliationLinkInput(item.EntryId, item.EntryRowVersion, 1)],
            "user-1",
            "User One"));

        Assert.True(result.Success);
        var saved = await db.ArppEntries.SingleAsync();
        Assert.Equal(1, saved.ProjectId);
        Assert.Equal("Indigenous swarm drones Algorithm", saved.ProjectReference);
        Assert.Contains("Arpp.EntriesReconciled", audit.Actions);
    }

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
