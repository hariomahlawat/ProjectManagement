using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
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
                    "17",
                    "Project Astra as issued",
                    1,
                    ArppCategory.Delisted,
                    25_000_000m,
                    "Comdt SDD",
                    "IR&D",
                    "9.3")
            ],
            "user-1",
            "User One"));

        Assert.True(save.Success);
        var entry = await db.ArppEntries.SingleAsync();
        Assert.Equal(ArppCategory.Delisted, entry.Category);
        Assert.Equal(25_000_000m, entry.IpaCost);
        Assert.Equal("Project Astra as issued", entry.ProjectReference);
        Assert.Equal(1, entry.ProjectId);
        Assert.Equal(10_000_000m, (await db.ProjectIpaFacts.SingleAsync()).IpaCost);
        Assert.Contains("Arpp.IssueCreated", audit.Actions);
        Assert.Contains("Arpp.WorkspaceSaved", audit.Actions);
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
                    "1",
                    "Delisted project",
                    null,
                    ArppCategory.Delisted,
                    1_000_000m,
                    "",
                    "",
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
        var issue = CreateIssue();
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "1",
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
        Assert.Contains("Arpp.IssueVerified", audit.Actions);
        Assert.Contains("Arpp.IssueUnlocked", audit.Actions);
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
            $"Project {serialNumber}",
            projectId,
            ArppCategory.New,
            1_000_000m,
            "Comdt SDD",
            "IR&D",
            "9.3");

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
