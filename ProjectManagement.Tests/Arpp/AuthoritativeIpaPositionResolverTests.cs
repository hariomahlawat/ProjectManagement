using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class AuthoritativeIpaPositionResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesLatestLinkedArppEntry_AndRetainsDelistedCost()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1);

        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 10_000_000m,
            CreatedByUserId = "legacy",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var original = CreateIssue(2025, ArppIssueKind.Original, 0, "ARPP 2025-26", new DateOnly(2025, 4, 10));
        original.Entries.Add(CreateEntry(1, 12_000_000m, ArppCategory.New, "11"));

        var addendum = CreateIssue(2026, ArppIssueKind.Addendum, 2, "Addendum No. 2", new DateOnly(2026, 7, 15));
        addendum.Entries.Add(CreateEntry(1, 15_500_000m, ArppCategory.Delisted, "17"));

        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var resolver = new AuthoritativeIpaPositionResolver(db);
        var result = await resolver.ResolveAsync(1);

        Assert.NotNull(result);
        Assert.Equal(IpaPositionSource.Arpp, result!.Source);
        Assert.Equal(15_500_000m, result.AmountInRupees);
        Assert.Equal(ArppCategory.Delisted, result.Category);
        Assert.True(result.IsDelisted);
        Assert.Equal(2026, result.FinancialYearStart);
        Assert.Equal(2, result.IssueSequence);
        Assert.Equal("17", result.SerialNumber);
    }

    [Fact]
    public async Task ResolveAsync_LaterIssueWithoutProject_DoesNotEraseEarlierPosition()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1);
        await SeedProjectAsync(db, 2);

        var original = CreateIssue(2026, ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 4, 1));
        original.Entries.Add(CreateEntry(1, 8_000_000m, ArppCategory.CarryForward, "4"));

        var addendum = CreateIssue(2026, ArppIssueKind.Addendum, 1, "Addendum No. 1", new DateOnly(2026, 6, 1));
        addendum.Entries.Add(CreateEntry(2, 9_000_000m, ArppCategory.New, "1"));

        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var resolver = new AuthoritativeIpaPositionResolver(db);
        var result = await resolver.ResolveAsync(1);

        Assert.NotNull(result);
        Assert.Equal(8_000_000m, result!.AmountInRupees);
        Assert.Equal(0, result.IssueSequence);
        Assert.Equal(ArppCategory.CarryForward, result.Category);
    }

    [Fact]
    public async Task ResolveManyAsync_UsesLegacyFallbackOnlyForUnlinkedProjects()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1);
        await SeedProjectAsync(db, 2);

        db.ProjectIpaFacts.AddRange(
            new ProjectIpaFact
            {
                ProjectId = 1,
                IpaCost = 5_000_000m,
                CreatedByUserId = "legacy",
                CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ProjectIpaFact
            {
                ProjectId = 2,
                IpaCost = 6_000_000m,
                CreatedByUserId = "legacy",
                CreatedOnUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        var issue = CreateIssue(2026, ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 4, 1));
        issue.Entries.Add(CreateEntry(1, 7_000_000m, ArppCategory.New, "3"));
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var resolver = new AuthoritativeIpaPositionResolver(db);
        var result = await resolver.ResolveManyAsync(new[] { 1, 2 });

        Assert.Equal(IpaPositionSource.Arpp, result[1].Source);
        Assert.Equal(7_000_000m, result[1].AmountInRupees);
        Assert.Equal(IpaPositionSource.LegacyProjectFact, result[2].Source);
        Assert.Equal(6_000_000m, result[2].AmountInRupees);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int id)
    {
        db.Projects.Add(new Project
        {
            Id = id,
            Name = $"Project {id}",
            CreatedByUserId = "seed"
        });
        await db.SaveChangesAsync();
    }

    private static ArppIssue CreateIssue(
        int financialYearStart,
        ArppIssueKind kind,
        int sequence,
        string name,
        DateOnly issueDate)
        => new()
        {
            FinancialYearStart = financialYearStart,
            Kind = kind,
            IssueSequence = sequence,
            Name = name,
            IssueDate = issueDate,
            CreatedAtUtc = new DateTimeOffset(issueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(issueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

    private static ArppEntry CreateEntry(
        int projectId,
        decimal ipaCost,
        ArppCategory category,
        string serialNumber)
        => new()
        {
            SortOrder = 1,
            SerialNumber = serialNumber,
            ProjectReference = $"Project {projectId}",
            ProjectId = projectId,
            Category = category,
            IpaCost = ipaCost,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };
}
