using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class HistoricalStageRecordServiceTests
{
    [Fact]
    public async Task GetEditorAsync_MapsExistingCancelledHistoryWithoutCreatingParallelData()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Cancelled);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = fixture.ProjectId,
                StageCode = StageCodes.FS,
                SortOrder = 0,
                Status = StageStatus.Completed,
                CompletedOn = new DateOnly(2024, 1, 10)
            },
            new ProjectStage
            {
                ProjectId = fixture.ProjectId,
                StageCode = StageCodes.SOW,
                SortOrder = 1,
                Status = StageStatus.InProgress,
                ActualStart = new DateOnly(2024, 1, 11)
            });
        await db.SaveChangesAsync();

        var editor = await fixture.Service.GetEditorAsync(fixture.ProjectId);

        Assert.True(editor.IsCancelled);
        Assert.Equal(
            HistoricalStageOutcome.Completed,
            editor.Rows.Single(row => row.StageCode == StageCodes.FS).Outcome);
        Assert.Equal(
            HistoricalStageOutcome.Ceased,
            editor.Rows.Single(row => row.StageCode == StageCodes.SOW).Outcome);
        Assert.Equal(
            ProcurementWorkflow.StageDefinitionsFor(PlanConstants.DefaultStageTemplateVersion).Length,
            editor.Rows.Count);
    }

    [Fact]
    public async Task SaveAsync_WritesStandardStagesAndAuditWithoutReopeningLifecycle()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Completed);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var input = ValidInput(fixture.ProjectId);
        input.EvidenceNote = "Project completion register, file 17/2024.";
        input.Rows.Single(row => row.StageCode == StageCodes.SOW).Outcome =
            HistoricalStageOutcome.Skipped;

        var result = await fixture.Service.SaveAsync(
            input,
            userId: "admin-1",
            userName: "Administrator");

        Assert.Equal(2, result.UpdatedCount);

        var project = await db.Projects.AsNoTracking().SingleAsync();
        Assert.Equal(ProjectLifecycleStatus.Completed, project.LifecycleStatus);
        Assert.True(project.IsLegacy);

        var stages = await db.ProjectStages
            .AsNoTracking()
            .OrderBy(stage => stage.SortOrder)
            .ToArrayAsync();
        Assert.Equal(2, stages.Length);
        Assert.Equal(StageStatus.Completed, stages[0].Status);
        Assert.Equal(new DateOnly(2024, 1, 10), stages[0].CompletedOn);
        Assert.Equal(StageStatus.Skipped, stages[1].Status);

        var logs = await db.StageChangeLogs.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, log => Assert.Equal("Backfill", log.Action));
        Assert.All(logs, log => Assert.Contains("Project completion register", log.Note ?? string.Empty));
        Assert.Equal("Projects.HistoricalStageHistoryUpdated", fixture.Audit.LastAction);
        Assert.Contains(StageCodes.FS, fixture.Audit.LastData["Stages"] ?? string.Empty);
        Assert.Contains(StageCodes.SOW, fixture.Audit.LastData["Stages"] ?? string.Empty);
    }

    [Fact]
    public async Task SaveAsync_RepresentsCancelledStageAsCeasedStandardStage()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Cancelled);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var input = ValidInput(fixture.ProjectId);
        input.EvidenceNote = "Cancellation board proceedings dated 20 January 2024.";
        var feasibility = input.Rows.Single(row => row.StageCode == StageCodes.FS);
        feasibility.Outcome = HistoricalStageOutcome.NotRecorded;
        feasibility.ActualStart = null;
        feasibility.CompletedOn = null;
        var sow = input.Rows.Single(row => row.StageCode == StageCodes.SOW);
        sow.Outcome = HistoricalStageOutcome.Ceased;
        sow.ActualStart = new DateOnly(2024, 1, 11);

        await fixture.Service.SaveAsync(
            input,
            userId: "hod-1",
            userName: "HoD");

        var stage = await db.ProjectStages.AsNoTracking().SingleAsync();
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(new DateOnly(2024, 1, 11), stage.ActualStart);
        Assert.Null(stage.CompletedOn);

        var project = await db.Projects.AsNoTracking().SingleAsync();
        Assert.Equal(ProjectLifecycleStatus.Cancelled, project.LifecycleStatus);
    }

    [Fact]
    public async Task SaveAsync_AllowsArppManagedIpaAfterCeasedStage_WithoutInventingStart()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Cancelled);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        AddPublishedPosition(
            db,
            fixture.ProjectId,
            issueDate: new DateOnly(2024, 1, 15));
        await db.SaveChangesAsync();

        var input = ValidInput(fixture.ProjectId);
        input.EvidenceNote = "Cancellation proceedings and published ARPP record.";

        var feasibility = input.Rows.Single(row => row.StageCode == StageCodes.FS);
        feasibility.Outcome = HistoricalStageOutcome.NotRecorded;
        feasibility.ActualStart = null;
        feasibility.CompletedOn = null;

        var sow = input.Rows.Single(row => row.StageCode == StageCodes.SOW);
        sow.Outcome = HistoricalStageOutcome.Ceased;
        sow.ActualStart = new DateOnly(2024, 1, 11);

        var ipa = input.Rows.Single(row => row.StageCode == StageCodes.IPA);
        ipa.Outcome = HistoricalStageOutcome.Completed;
        ipa.ActualStart = null;
        ipa.CompletedOn = new DateOnly(2024, 1, 15);

        var service = new HistoricalStageRecordService(
            db,
            new WorkflowStageMetadataProvider(),
            new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            fixture.Audit,
            NullLogger<HistoricalStageRecordService>.Instance,
            new ArppIpaStageAuthorityService(db));

        await service.SaveAsync(
            input,
            userId: "hod-1",
            userName: "HoD");

        var stages = await db.ProjectStages
            .AsNoTracking()
            .ToDictionaryAsync(stage => stage.StageCode);

        Assert.Equal(StageStatus.InProgress, stages[StageCodes.SOW].Status);
        Assert.Equal(StageStatus.Completed, stages[StageCodes.IPA].Status);
        Assert.Equal(new DateOnly(2024, 1, 15), stages[StageCodes.IPA].CompletedOn);
        Assert.Null(stages[StageCodes.IPA].ActualStart);
        Assert.False(stages[StageCodes.IPA].RequiresBackfill);
    }

    [Fact]
    public async Task SaveAsync_RejectsActiveOrNonLegacyProjects()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Active);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        await Assert.ThrowsAsync<HistoricalStageRecordNotAllowedException>(() =>
            fixture.Service.SaveAsync(
                ValidInput(fixture.ProjectId),
                userId: "admin-1",
                userName: "Administrator"));
    }

    [Fact]
    public async Task SaveAsync_RejectsDatesAfterKnownLifecycleDate()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Cancelled);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var input = ValidInput(fixture.ProjectId);
        input.Rows.Single(row => row.StageCode == StageCodes.FS).CompletedOn =
            new DateOnly(2024, 1, 21);

        var exception = await Assert.ThrowsAsync<HistoricalStageRecordValidationException>(() =>
            fixture.Service.SaveAsync(
                input,
                userId: "admin-1",
                userName: "Administrator"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("project lifecycle date", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.ProjectStages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveAsync_UsesYearOnlyCompletionAsHistoricalDateUpperBound()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Completed);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var project = await db.Projects.SingleAsync();
        project.CompletedOn = null;
        project.CompletedYear = 1998;
        project.CompletedMonth = null;
        await db.SaveChangesAsync();

        var input = ValidInput(fixture.ProjectId);
        var feasibility = input.Rows.Single(row => row.StageCode == StageCodes.FS);
        feasibility.ActualStart = new DateOnly(1998, 12, 1);
        feasibility.CompletedOn = new DateOnly(1999, 1, 1);

        var exception = await Assert.ThrowsAsync<HistoricalStageRecordValidationException>(() =>
            fixture.Service.SaveAsync(
                input,
                userId: "admin-1",
                userName: "Administrator"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("project lifecycle date", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.ProjectStages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveAsync_RejectsDeletedLegacyProject()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Completed);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var project = await db.Projects.SingleAsync();
        project.IsDeleted = true;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<HistoricalStageRecordNotAllowedException>(() =>
            fixture.Service.SaveAsync(
                ValidInput(fixture.ProjectId),
                userId: "admin-1",
                userName: "Administrator"));

        Assert.Empty(await db.ProjectStages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveAsync_RollsBackStageChangesWhenFormalAuditCannotBeWritten()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Completed);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var service = new HistoricalStageRecordService(
            db,
            new WorkflowStageMetadataProvider(),
            new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new ThrowingAudit(),
            NullLogger<HistoricalStageRecordService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(
                ValidInput(fixture.ProjectId),
                userId: "admin-1",
                userName: "Administrator"));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.ProjectStages.AsNoTracking().ToListAsync());
        Assert.Empty(await db.StageChangeLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveAsync_RejectsIncompleteWorkflowSubmission()
    {
        var fixture = await CreateFixtureAsync(ProjectLifecycleStatus.Completed);
        await using var connection = fixture.Connection;
        await using var db = fixture.Db;

        var input = ValidInput(fixture.ProjectId);
        input.Rows.RemoveAt(input.Rows.Count - 1);

        var exception = await Assert.ThrowsAsync<HistoricalStageRecordValidationException>(() =>
            fixture.Service.SaveAsync(
                input,
                userId: "admin-1",
                userName: "Administrator"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("stage list is incomplete", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.ProjectStages.AsNoTracking().ToListAsync());
    }

    private static void AddPublishedPosition(
        ApplicationDbContext db,
        int projectId,
        DateOnly issueDate)
    {
        var issue = new ArppIssue
        {
            FinancialYearStart = issueDate.Year,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP/I&R&D/ARTRAC",
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
            RevisionNumber = 1,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = "arpp/history-test.pdf",
            AttachmentOriginalFileName = "history-test.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    SourceEntryId = 1,
                    SortOrder = 1,
                    SerialNumber = "33",
                    ProjectReference = "Legacy history test",
                    ProjectId = projectId,
                    Category = ArppCategory.Delisted,
                    IpaCost = 5_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3"
                }
            }
        };

        db.ArppIssues.Add(issue);
    }

    private static HistoricalStageRecordInput ValidInput(int projectId)
    {
        var rows = ProcurementWorkflow
            .StageDefinitionsFor(PlanConstants.DefaultStageTemplateVersion)
            .Select(definition => new HistoricalStageRecordRowInput
            {
                StageCode = definition.Code,
                Outcome = HistoricalStageOutcome.NotRecorded
            })
            .ToList();

        var feasibility = rows.Single(row => row.StageCode == StageCodes.FS);
        feasibility.Outcome = HistoricalStageOutcome.Completed;
        feasibility.ActualStart = new DateOnly(2024, 1, 1);
        feasibility.CompletedOn = new DateOnly(2024, 1, 10);

        return new HistoricalStageRecordInput
        {
            ProjectId = projectId,
            EvidenceNote = "Verified project register.",
            Rows = rows
        };
    }

    private static async Task<TestFixture> CreateFixtureAsync(
        ProjectLifecycleStatus lifecycleStatus)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var project = new Project
        {
            Name = "Legacy history test",
            CreatedByUserId = "admin-1",
            IsLegacy = true,
            LifecycleStatus = lifecycleStatus,
            WorkflowVersion = PlanConstants.DefaultStageTemplateVersion,
            CompletedOn = lifecycleStatus == ProjectLifecycleStatus.Completed
                ? new DateOnly(2024, 1, 20)
                : null,
            CancelledOn = lifecycleStatus == ProjectLifecycleStatus.Cancelled
                ? new DateOnly(2024, 1, 20)
                : null
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var audit = new CapturingAudit();
        var clock = new FixedClock(
            new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new HistoricalStageRecordService(
            db,
            new WorkflowStageMetadataProvider(),
            clock,
            audit,
            NullLogger<HistoricalStageRecordService>.Instance);

        return new TestFixture(connection, db, service, audit, project.Id);
    }

    private sealed record TestFixture(
        SqliteConnection Connection,
        ApplicationDbContext Db,
        HistoricalStageRecordService Service,
        CapturingAudit Audit,
        int ProjectId);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class CapturingAudit : IAuditService
    {
        public string? LastAction { get; private set; }

        public IReadOnlyDictionary<string, string?> LastData { get; private set; } =
            new Dictionary<string, string?>();

        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            LastAction = action;
            LastData = data is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => throw new InvalidOperationException("Audit store unavailable.");
    }
}
