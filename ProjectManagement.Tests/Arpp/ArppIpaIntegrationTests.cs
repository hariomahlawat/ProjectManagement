using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppIpaIntegrationTests
{
    [Fact]
    public async Task ProcurementAndCostResolvers_UseLinkedArppAmountWithoutUpdatingLegacyFact()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            CostLakhs = 0m
        });
        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 10_000_000m,
            CreatedByUserId = "legacy",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var issue = new ArppIssue
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Addendum,
            IssueSequence = 1,
            Name = "Addendum No. 1",
            IssueDate = new DateOnly(2026, 6, 1),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = null,
            PppNumber = null,
            ProjectReference = "Project",
            ProjectId = 1,
            Category = ArppCategory.Delisted,
            IpaCost = 25_000_000m,
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

        var sourceEntry = issue.Entries.Single();
        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            ArppIssueId = issue.Id,
            RevisionNumber = 1,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issue.IssueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = "published/arpp.pdf",
            AttachmentOriginalFileName = "ARPP.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    SourceEntryId = sourceEntry.Id,
                    SortOrder = sourceEntry.SortOrder,
                    SerialNumber = sourceEntry.SerialNumber,
                    PppNumber = sourceEntry.PppNumber,
                    ProjectReference = sourceEntry.ProjectReference,
                    ProjectId = sourceEntry.ProjectId,
                    Category = sourceEntry.Category,
                    IpaCost = sourceEntry.IpaCost,
                    Cfa = sourceEntry.Cfa,
                    Fund = sourceEntry.Fund,
                    DfpdsSchedule = sourceEntry.DfpdsSchedule
                }
            }
        };
        await db.SaveChangesAsync();

        var procurement = await new ProjectProcurementReadService(db).GetAsync(1);
        var projectCost = await new ProjectCostResolver(db).ResolveCostInCrAsync(new[] { 1 });
        var briefingCost = await new ProjectBriefingCostResolver(db).ResolveCostRdAsync(new[] { 1 });
        var hasIpaPosition = await new ProjectFactsReadService(db)
            .HasRequiredFactsAsync(1, StageCodes.IPA);

        Assert.Equal(25_000_000m, procurement.IpaCost);
        Assert.True(procurement.IsIpaManagedByArpp);
        Assert.True(procurement.IpaPosition!.IsDelisted);

        Assert.Equal(ProjectCostSource.Ipa, projectCost[1].Source);
        Assert.Equal(2.5m, projectCost[1].CostInCr);

        Assert.Equal(ProjectBriefingCostBasis.IPA, briefingCost[1].Basis);
        Assert.Equal(25_000_000m, briefingCost[1].AmountInRupees);
        Assert.True(hasIpaPosition);

        Assert.Equal(10_000_000m, (await db.ProjectIpaFacts.SingleAsync()).IpaCost);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
