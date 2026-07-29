using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppCommandServiceTests
{
    [Fact]
    public async Task CreateAndSaveWorkspace_PersistsCompleteDelistedRow_WithoutChangingLegacyIpa()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Astra",
            CaseFileNumber = "SDD/1",
            CreatedByUserId = "seed"
        });
        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 10_000_000m,
            CreatedByUserId = "legacy",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var audit = new FakeAuditService();
        var service = new ArppCommandService(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)),
            audit);

        var create = await service.CreateIssueAsync(new ArppIssueCreateCommand(
            2026,
            ArppIssueKind.Original,
            0,
            "ARPP 2026-27",
            new DateOnly(2026, 4, 10),
            "user-1",
            "User One"));

        Assert.True(create.Success);
        var issue = await db.ArppIssues.SingleAsync();

        var save = await service.SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            2026,
            ArppIssueKind.Original,
            0,
            "ARPP 2026-27",
            new DateOnly(2026, 4, 10),
            [
                new ArppEntryInput(
                    null,
                    null,
                    null,
                    null,
                    "Project Astra as issued",
                    1,
                    ArppCategory.Delisted,
                    25_000_000m,
                    null,
                    "Comdt SDD",
                    null,
                    "IR&D",
                    null,
                    "9.3")
            ],
            "user-1",
            "User One"));

        Assert.True(save.Success);
        var entry = await db.ArppEntries.SingleAsync();
        Assert.Equal(ArppCategory.Delisted, entry.Category);
        Assert.Null(entry.SerialNumber);
        Assert.Null(entry.PppNumber);
        Assert.Equal(25_000_000m, entry.IpaCost);
        Assert.Equal("Project Astra as issued", entry.ProjectReference);
        Assert.Equal(1, entry.ProjectId);
        Assert.Equal(10_000_000m, (await db.ProjectIpaFacts.SingleAsync()).IpaCost);
        Assert.Contains("Arpp.IssueCreated", audit.Actions);
        Assert.Contains("Arpp.WorkspaceSaved", audit.Actions);
    }

    [Fact]
    public async Task SaveWorkspace_SelectedReferenceOptions_AreResolvedServerSide()
    {
        await using var db = CreateContext();
        db.ArppCfaOptions.Add(CreateCfaOption());
        db.ArppFundOptions.Add(CreateFundOption());
        db.ArppDfpdsSchedules.Add(CreateDfpdsSchedule());
        var issue = CreateIssue();
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.Name,
            issue.IssueDate,
            [
                new ArppEntryInput(
                    null,
                    null,
                    "1",
                    "ARPP/IR&D/N/2026-27/1",
                    "Project One",
                    null,
                    ArppCategory.New,
                    1_000_000m,
                    1,
                    "spoofed CFA",
                    1,
                    "spoofed Fund",
                    1,
                    "spoofed schedule")
            ],
            "user-1",
            "User One"));

        Assert.True(result.Success);
        var saved = await db.ArppEntries.SingleAsync();
        Assert.Equal(1, saved.CfaOptionId);
        Assert.Equal("Comdt SDD", saved.Cfa);
        Assert.Equal(1, saved.FundOptionId);
        Assert.Equal("IR&D", saved.Fund);
        Assert.Equal(1, saved.DfpdsScheduleId);
        Assert.Equal("9.3", saved.DfpdsSchedule);
    }


    [Fact]
    public async Task SaveWorkspace_ApprovedRowRequiresSerialAndPppNumber()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.Name,
            issue.IssueDate,
            [
                new ArppEntryInput(
                    null,
                    null,
                    "1",
                    null,
                    "Approved project",
                    null,
                    ArppCategory.New,
                    1_000_000m,
                    null,
                    "Comdt SDD",
                    null,
                    "IR&D",
                    null,
                    "9.3")
            ],
            "user-1",
            "User One"));

        Assert.False(result.Success);
        Assert.Contains("Entries[0].PppNumber", result.FieldErrors.Keys);
        Assert.Empty(await db.ArppEntries.ToListAsync());
    }

    [Fact]
    public async Task SaveWorkspace_DuplicateLinkedProject_IsRejectedBeforePersistence()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project", CreatedByUserId = "seed" });
        var issue = CreateIssue();
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.Name,
            issue.IssueDate,
            [
                CompleteInput("1", 1),
                CompleteInput("2", 1)
            ],
            "user-1",
            "User One"));

        Assert.False(result.Success);
        Assert.Contains(result.FieldErrors.Keys, key => key.EndsWith("ProjectId", StringComparison.Ordinal));
        Assert.Empty(await db.ArppEntries.ToListAsync());
    }

    [Fact]
    public async Task SaveWorkspace_DelistedRowStillRequiresAllNormalFields()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.Name,
            issue.IssueDate,
            [
                new ArppEntryInput(
                    null,
                    null,
                    null,
                    null,
                    "Delisted project",
                    null,
                    ArppCategory.Delisted,
                    1_000_000m,
                    null,
                    "",
                    null,
                    "",
                    null,
                    "")
            ],
            "user-1",
            "User One"));

        Assert.False(result.Success);
        Assert.Contains("Entries[0].Cfa", result.FieldErrors.Keys);
        Assert.Contains("Entries[0].Fund", result.FieldErrors.Keys);
        Assert.Contains("Entries[0].DfpdsSchedule", result.FieldErrors.Keys);
        Assert.Empty(await db.ArppEntries.ToListAsync());
    }


    [Fact]
    public async Task CreateIssue_OriginalIssuedBeforeFinancialYear_DoesNotRaiseMisleadingWarning()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var result = await service.CreateIssueAsync(new ArppIssueCreateCommand(
            2026,
            ArppIssueKind.Original,
            0,
            "ARPP 2026-27",
            new DateOnly(2026, 2, 26),
            "user-1",
            "User One"));

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Contains("outside", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("unusually distant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveWorkspace_CanRemoveTheFinalIssuedRow()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
            PppNumber = "ARPP/IR&D/N/2026-27/1",
            ProjectReference = "Project 1",
            Category = ArppCategory.New,
            IpaCost = 1_000_000m,
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

        var result = await CreateService(db).SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.Name,
            issue.IssueDate,
            [],
            "user-1",
            "User One"));

        Assert.True(result.Success);
        Assert.Empty(await db.ArppEntries.ToListAsync());
    }

    [Fact]
    public async Task VerifyAndUnlock_ProtectsIssuedDataAndAuditsBothActions()
    {
        await using var db = CreateContext();
        db.ArppCfaOptions.Add(CreateCfaOption());
        db.ArppFundOptions.Add(CreateFundOption());
        db.ArppDfpdsSchedules.Add(CreateDfpdsSchedule());
        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
            PppNumber = "ARPP/IR&D/N/2026-27/1",
            ProjectReference = "Project 1",
            Category = ArppCategory.New,
            IpaCost = 1_000_000m,
            CfaOptionId = 1,
            Cfa = "Comdt SDD",
            FundOptionId = 1,
            Fund = "IR&D",
            DfpdsScheduleId = 1,
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/test.pdf",
            OriginalFileName = "ARPP.pdf",
            ContentType = "application/pdf",
            SizeBytes = 123,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var audit = new FakeAuditService();
        var service = new ArppCommandService(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)),
            audit);

        var verify = await service.VerifyAsync(new ArppVerifyCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            "Checked against the issued HQ PDF.",
            "verifier-1",
            "Verifier One"));

        Assert.True(verify.Success);
        var verified = await db.ArppIssues.SingleAsync();
        Assert.True(verified.IsVerified);
        Assert.NotNull(verified.VerifiedAtUtc);
        Assert.Equal("verifier-1", verified.VerifiedByUserId);

        var published = await db.ArppPublishedIssues
            .Include(snapshot => snapshot.Entries)
            .SingleAsync();
        Assert.Equal(1, published.RevisionNumber);
        Assert.Equal("ARPP.pdf", published.AttachmentOriginalFileName);
        Assert.Single(published.Entries);
        Assert.Equal("Project 1", published.Entries.Single().ProjectReference);
        Assert.Equal("1", published.Entries.Single().SerialNumber);
        Assert.Equal("ARPP/IR&D/N/2026-27/1", published.Entries.Single().PppNumber);

        var blockedSave = await service.SaveWorkspaceAsync(new ArppWorkspaceSaveCommand(
            verified.Id,
            Convert.ToBase64String(verified.RowVersion),
            verified.FinancialYearStart,
            verified.Kind,
            verified.IssueSequence,
            "Changed name",
            verified.IssueDate,
            [],
            "user-1",
            "User One"));
        Assert.False(blockedSave.Success);
        Assert.Contains("verified and locked", blockedSave.Message, StringComparison.OrdinalIgnoreCase);

        var unlock = await service.UnlockAsync(new ArppUnlockCommand(
            verified.Id,
            Convert.ToBase64String(verified.RowVersion),
            "Correction required after comparison with the signed document.",
            "hod-1",
            "HoD One"));

        Assert.True(unlock.Success);
        var unlocked = await db.ArppIssues.SingleAsync();
        Assert.False(unlocked.IsVerified);
        Assert.Null(unlocked.VerifiedAtUtc);
        Assert.Null(unlocked.VerifiedByUserId);

        var stillPublished = await db.ArppPublishedIssues
            .Include(snapshot => snapshot.Entries)
            .SingleAsync();
        Assert.Equal(1, stillPublished.RevisionNumber);
        Assert.Single(stillPublished.Entries);
        Assert.Equal("Project 1", stillPublished.Entries.Single().ProjectReference);

        Assert.Contains("Arpp.IssueVerified", audit.Actions);
        Assert.Contains("Arpp.IssueUnlocked", audit.Actions);
    }

    [Fact]
    public async Task Verify_LinkedPublishedRow_CompletesIpaStageOnIssuedDocumentDate()
    {
        await using var db = CreateContext();
        db.ArppCfaOptions.Add(CreateCfaOption());
        db.ArppFundOptions.Add(CreateFundOption());
        db.ArppDfpdsSchedules.Add(CreateDfpdsSchedule());
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project One",
            CreatedByUserId = "seed"
        });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = ProcurementWorkflow.OrderOf(null, StageCodes.IPA),
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = new DateOnly(2026, 4, 8)
        });

        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
            PppNumber = "ARPP/IR&D/N/2026-27/1",
            ProjectReference = "Project One as issued",
            ProjectId = 1,
            Category = ArppCategory.New,
            IpaCost = 1_000_000m,
            CfaOptionId = 1,
            Cfa = "Comdt SDD",
            FundOptionId = 1,
            Fund = "IR&D",
            DfpdsScheduleId = 1,
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/test.pdf",
            OriginalFileName = "ARPP.pdf",
            ContentType = "application/pdf",
            SizeBytes = 123,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var audit = new FakeAuditService();
        var service = new ArppCommandService(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)),
            audit);

        var result = await service.VerifyAsync(new ArppVerifyCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            null,
            "verifier-1",
            "Verifier One"));

        Assert.True(result.Success);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(issue.IssueDate, stage.CompletedOn);
        Assert.Equal(new DateOnly(2026, 1, 1), stage.ActualStart);
        Assert.Contains("Arpp.IpaStageSynchronized", audit.Actions);
    }

    [Fact]
    public async Task Verify_RequiresRowsAndIssuedPdf()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).VerifyAsync(new ArppVerifyCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            null,
            "verifier-1",
            "Verifier One"));

        Assert.False(result.Success);
        Assert.Contains("Entries", result.FieldErrors.Keys);
        Assert.Contains("Attachment", result.FieldErrors.Keys);
    }


    [Fact]
    public async Task Verify_BlocksLegacyApprovedRowUntilPppNumberIsBackfilled()
    {
        await using var db = CreateContext();
        db.ArppCfaOptions.Add(CreateCfaOption());
        db.ArppFundOptions.Add(CreateFundOption());
        db.ArppDfpdsSchedules.Add(CreateDfpdsSchedule());
        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
            PppNumber = null,
            ProjectReference = "Legacy approved row",
            Category = ArppCategory.New,
            IpaCost = 1_000_000m,
            CfaOptionId = 1,
            Cfa = "Comdt SDD",
            FundOptionId = 1,
            Fund = "IR&D",
            DfpdsScheduleId = 1,
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/test.pdf",
            OriginalFileName = "ARPP.pdf",
            ContentType = "application/pdf",
            SizeBytes = 123,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).VerifyAsync(new ArppVerifyCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            null,
            "verifier-1",
            "Verifier One"));

        Assert.False(result.Success);
        Assert.Contains("IssuedIdentifiers", result.FieldErrors.Keys);
        Assert.False((await db.ArppIssues.SingleAsync()).IsVerified);
    }

    [Fact]
    public async Task Verify_BlocksRowsWithUnmappedControlledReferenceValues()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
            PppNumber = "ARPP/IR&D/N/2026-27/1",
            ProjectReference = "Project One",
            Category = ArppCategory.New,
            IpaCost = 1_000_000m,
            Cfa = "New CFA exactly as issued",
            Fund = "New Fund",
            DfpdsSchedule = "10.1",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/test.pdf",
            OriginalFileName = "ARPP.pdf",
            ContentType = "application/pdf",
            SizeBytes = 123,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).VerifyAsync(new ArppVerifyCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            null,
            "verifier-1",
            "Verifier One"));

        Assert.False(result.Success);
        Assert.Contains("ReferenceData", result.FieldErrors.Keys);
        Assert.False((await db.ArppIssues.SingleAsync()).IsVerified);
    }

    [Fact]
    public async Task Unlock_RejectsShortNonMeaningfulReason()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.IsVerified = true;
        issue.VerifiedAtUtc = DateTimeOffset.UtcNow;
        issue.VerifiedByUserId = "verifier-1";
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).UnlockAsync(new ArppUnlockCommand(
            issue.Id,
            Convert.ToBase64String(issue.RowVersion),
            "add new",
            "hod-1",
            "HoD One"));

        Assert.False(result.Success);
        Assert.Contains(nameof(ArppUnlockCommand.Reason), result.FieldErrors.Keys);
        Assert.Contains(
            result.FieldErrors[nameof(ArppUnlockCommand.Reason)],
            message => message.Contains("at least 10 characters", StringComparison.OrdinalIgnoreCase));
        Assert.True((await db.ArppIssues.SingleAsync()).IsVerified);
    }

    private static ArppCommandService CreateService(ApplicationDbContext db)
        => new(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)),
            new FakeAuditService());

    private static ArppEntryInput CompleteInput(string serialNumber, int? projectId)
        => new(
            null,
            null,
            serialNumber,
            $"ARPP/IR&D/N/2026-27/{serialNumber}",
            $"Project {serialNumber}",
            projectId,
            ArppCategory.New,
            1_000_000m,
            null,
            "Comdt SDD",
            null,
            "IR&D",
            null,
            "9.3");

    private static ArppCfaOption CreateCfaOption() => new()
    {
        Id = 1,
        Name = "Comdt SDD",
        NormalizedName = "COMDT SDD",
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        CreatedByUserId = "seed",
        UpdatedByUserId = "seed"
    };

    private static ArppFundOption CreateFundOption() => new()
    {
        Id = 1,
        Name = "IR&D",
        NormalizedName = "IR&D",
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        CreatedByUserId = "seed",
        UpdatedByUserId = "seed"
    };

    private static ArppDfpdsSchedule CreateDfpdsSchedule() => new()
    {
        Id = 1,
        Code = "9.3",
        NormalizedCode = "9.3",
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        CreatedByUserId = "seed",
        UpdatedByUserId = "seed"
    };

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
