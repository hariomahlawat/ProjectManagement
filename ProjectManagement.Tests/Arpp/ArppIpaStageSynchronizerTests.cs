using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppIpaStageSynchronizerTests
{
    [Fact]
    public async Task SynchronizeProjects_UsesEarliestPublishedIssueDate_AndOverridesExistingStageDate()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project AURA",
            CreatedByUserId = "seed"
        });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = ProcurementWorkflow.OrderOf(null, StageCodes.IPA),
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = new DateOnly(2026, 2, 24),
            IsAutoCompleted = true,
            AutoCompletedFromCode = StageCodes.AON,
            RequiresBackfill = true
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 2, 26), 0, ArppCategory.New);
        AddPublishedPosition(db, 2, 1, new DateOnly(2026, 6, 15), 1, ArppCategory.CommittedLiability);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(1, change.ProjectId);
        Assert.Equal(new DateOnly(2026, 2, 26), change.CompletionDate);
        Assert.Equal(new DateOnly(2026, 2, 24), change.PreviousCompletedOn);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
        Assert.Equal(new DateOnly(2026, 1, 1), stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task SynchronizeProjects_CreatesMissingIpaStage_AndUsesExactIssuedDate()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project One",
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            CreatedByUserId = "seed"
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.Delisted);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var change = Assert.Single(result.Changes);
        Assert.True(change.StageCreated);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageCodes.IPA, stage.StageCode);
        Assert.Equal(ProcurementWorkflow.OrderOf(ProcurementWorkflow.VersionV2, StageCodes.IPA), stage.SortOrder);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.CompletedOn);
    }

    [Fact]
    public async Task SynchronizeProjects_CorrectsActualStartThatFallsAfterAuthoritativeCompletion()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 5, 1)
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.CarryForward);
        await db.SaveChangesAsync();

        await new ArppIpaStageSynchronizer(db).SynchronizeProjectsAsync([1]);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 4, 10), stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.CompletedOn);
        Assert.Equal(StageStatus.Completed, stage.Status);
    }

    [Fact]
    public async Task SynchronizeProjects_IgnoresWorkingRowsWithoutPublishedSnapshot()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.NotStarted
        });
        var issue = new ArppIssue
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "Working ARPP",
            IssueDate = new DateOnly(2026, 4, 10),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed",
            Entries =
            {
                new ArppEntry
                {
                    SortOrder = 1,
                    SerialNumber = "1",
                    ProjectReference = "Project One",
                    ProjectId = 1,
                    Category = ArppCategory.New,
                    IpaCost = 1_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = "seed",
                    UpdatedByUserId = "seed"
                }
            }
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Empty(result.Changes);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.NotStarted, stage.Status);
        Assert.Null(stage.CompletedOn);
    }

    [Fact]
    public async Task SynchronizeProjects_IsIdempotent()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var service = new ArppIpaStageSynchronizer(db);
        var first = await service.SynchronizeProjectsAsync([1]);
        var second = await service.SynchronizeProjectsAsync([1]);

        Assert.Single(first.Changes);
        Assert.Empty(second.Changes);
        Assert.Single(await db.ProjectStages.ToListAsync());
    }

    private static void AddPublishedPosition(
        ApplicationDbContext db,
        long issueId,
        int projectId,
        DateOnly issueDate,
        int issueSequence,
        ArppCategory category)
    {
        var issue = new ArppIssue
        {
            Id = issueId,
            FinancialYearStart = 2026,
            Kind = issueSequence == 0 ? ArppIssueKind.Original : ArppIssueKind.Addendum,
            IssueSequence = issueSequence,
            Name = issueSequence == 0 ? "Original ARPP" : $"Addendum {issueSequence}",
            IssueDate = issueDate,
            IsVerified = true,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            VerifiedByUserId = "verifier",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            ArppIssueId = issueId,
            RevisionNumber = 1,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = $"arpp/{issueId}.pdf",
            AttachmentOriginalFileName = $"ARPP-{issueId}.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    ArppIssueId = issueId,
                    SourceEntryId = issueId,
                    SortOrder = 1,
                    SerialNumber = issueId.ToString(),
                    ProjectReference = $"Project {projectId}",
                    ProjectId = projectId,
                    Category = category,
                    IpaCost = 1_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3"
                }
            }
        };

        db.ArppIssues.Add(issue);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
