using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppReadServiceTests
{
    [Fact]
    public async Task GetProjectHistoryAsync_MarksLatestIssueAsAuthoritative()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project", CreatedByUserId = "seed" });

        var original = CreateIssue(2025, 0, ArppIssueKind.Original, "ARPP 2025-26");
        original.Entries.Add(CreateEntry(1, "4", ArppCategory.New, 10_000_000m));
        var addendum = CreateIssue(2026, 2, ArppIssueKind.Addendum, "Addendum No. 2");
        addendum.Entries.Add(CreateEntry(1, "17", ArppCategory.Delisted, 15_000_000m));
        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var history = await new ArppReadService(db).GetProjectHistoryAsync(1);

        Assert.NotNull(history);
        Assert.Equal(2, history!.Items.Count);
        Assert.True(history.Items[0].IsAuthoritative);
        Assert.Equal(2026, history.Items[0].FinancialYearStart);
        Assert.Equal(ArppCategory.Delisted, history.Items[0].Category);
        Assert.Equal(15_000_000m, history.Items[0].IpaCost);
        Assert.False(history.Items[1].IsAuthoritative);
    }

    private static ArppIssue CreateIssue(int year, int sequence, ArppIssueKind kind, string name)
        => new()
        {
            FinancialYearStart = year,
            Kind = kind,
            IssueSequence = sequence,
            Name = name,
            IssueDate = new DateOnly(year, 4, 10),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

    private static ArppEntry CreateEntry(int projectId, string serial, ArppCategory category, decimal cost)
        => new()
        {
            SortOrder = 1,
            SerialNumber = serial,
            ProjectReference = "Project",
            ProjectId = projectId,
            Category = category,
            IpaCost = cost,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
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
}
