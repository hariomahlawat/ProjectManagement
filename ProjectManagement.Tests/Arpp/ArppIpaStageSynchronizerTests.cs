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
    public async Task SynchronizeProjects_UsesEarliestPublishedIssueDate_AndPreservesValidActualStart()
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
        AddPublishedPosition(db, 2, 1, new DateOnly(2026, 7, 27), 1, ArppCategory.CarryForward);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new DateOnly(2026, 2, 26), change.CompletionDate);
        Assert.Equal(1, change.SourceIssueId);
        Assert.Equal("Original ARPP", change.SourceDocumentLabel);
        Assert.Empty(result.DataQualityIssues);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
        Assert.Equal(new DateOnly(2026, 1, 1), stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task SynchronizeProjects_CreatesMissingIpaStage_WithoutInventingActualStart()
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
        Assert.Empty(result.DataQualityIssues);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageCodes.IPA, stage.StageCode);
        Assert.Equal(ProcurementWorkflow.OrderOf(ProcurementWorkflow.VersionV2, StageCodes.IPA), stage.SortOrder);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task SynchronizeProjects_PreservesInvalidActualStart_AndRecordsDataQualityIssueOnce()
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

        var first = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var issue = Assert.Single(first.DataQualityIssues);
        Assert.Equal(new DateOnly(2026, 5, 1), issue.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), issue.CompletionDate);

        db.AuditLogs.Add(new AuditLog
        {
            Action = "Arpp.IpaStageDataQualityIssue",
            Level = "Warning",
            Message = "Recorded IPA actual start is later than the authoritative completion date.",
            DataJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["ProjectId"] = "1",
                ["ActualStart"] = "2026-05-01",
                ["CompletionDate"] = "2026-04-10"
            })
        });
        await db.SaveChangesAsync();

        var second = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);
        Assert.Empty(second.DataQualityIssues);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 5, 1), stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.CompletedOn);
        Assert.Equal(StageStatus.Completed, stage.Status);

        Assert.DoesNotContain(
            await db.StageChangeLogs.ToListAsync(),
            log => string.Equals(log.Action, "ArppDataQuality", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SynchronizeProjects_LaterAddendumDoesNotMoveInitialIpaCompletionDate()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 2, 26), 0, ArppCategory.New);
        AddPublishedPosition(db, 2, 1, new DateOnly(2026, 7, 27), 1, ArppCategory.CarryForward);
        await db.SaveChangesAsync();

        await new ArppIpaStageSynchronizer(db).SynchronizeProjectsAsync([1]);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
        Assert.Null(stage.ActualStart);
    }

    [Fact]
    public async Task SynchronizeProjects_SupersedesPendingManualIpaRequest()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 1, 1)
        });
        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = new DateOnly(2026, 3, 1),
            RequestedByUserId = "po-1",
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 2, 26), 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Equal(1, result.SupersededRequestCount);
        var request = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("Superseded", request.DecisionStatus);
        Assert.Equal("PRISM system", request.DecidedByUserId);
        Assert.NotNull(request.DecidedOn);
        Assert.Contains("published ARPP records became authoritative", request.DecisionNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await db.StageChangeLogs.ToListAsync(), log =>
            log.Action == "Superseded" &&
            log.StageCode == StageCodes.IPA);
    }

    [Fact]
    public async Task SynchronizeProjects_SupersedesPendingRequest_WhenStageAlreadyMatchesAuthority()
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 2, 26);
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.Completed,
            CompletedOn = authorityDate
        });
        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = authorityDate.AddDays(2),
            RequestedByUserId = "po-1",
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        });
        AddPublishedPosition(db, 1, 1, authorityDate, 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Empty(result.Changes);
        Assert.Equal(1, result.SupersededRequestCount);
        Assert.Equal("Superseded", (await db.StageChangeRequests.SingleAsync()).DecisionStatus);
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
        db.ArppIssues.Add(new ArppIssue
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
                    PppNumber = "ARPP/IR&D/N/2026-27/1",
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
        });
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Empty(result.Changes);
        Assert.Empty(result.DataQualityIssues);
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
        Assert.Empty(second.DataQualityIssues);
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
            Name = issueSequence == 0 ? "ARPP/I&R&D/ARTRAC" : "ARPP/I&R&D/ARTRAC",
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
                    SerialNumber = category == ArppCategory.Delisted ? null : "33",
                    PppNumber = category == ArppCategory.Delisted ? null : "ARPP/IR&D/2026-27/33",
                    ProjectReference = $"Project {projectId}",
                    ProjectId = projectId,
                    Category = category,
                    IpaCost = issueSequence == 0 ? 40_000_000m : 50_000_000m,
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
