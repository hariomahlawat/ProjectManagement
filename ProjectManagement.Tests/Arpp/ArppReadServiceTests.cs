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


    [Fact]
    public async Task GetIssueAndRegisterAsync_ExposeIssuedPdfMetadata()
    {
        await using var db = CreateContext();
        var issue = CreateIssue(2026, 0, ArppIssueKind.Original, "ARPP 2026-27");
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/1/document.pdf",
            OriginalFileName = "ARPP-2026-27.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1234,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = new ArppReadService(db);
        var details = await service.GetIssueAsync(issue.Id);
        var register = await service.GetRegisterAsync(null, null);

        Assert.NotNull(details?.Attachment);
        Assert.Equal("ARPP-2026-27.pdf", details!.Attachment!.OriginalFileName);
        Assert.True(register.FinancialYears.Single().Issues.Single().HasAttachment);
    }


    [Fact]
    public async Task GetRegisterAsync_UsesLatestLinkedPositionInsteadOfAddingRepeatedAddendumRows()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Repeated project", CreatedByUserId = "seed" });

        var original = CreateIssue(2026, 0, ArppIssueKind.Original, "ARPP 2026-27");
        original.Entries.Add(CreateEntry(1, "1", ArppCategory.New, 10_000_000m));
        original.Entries.Add(new ArppEntry
        {
            SortOrder = 2,
            SerialNumber = "2",
            PppNumber = "ARPP/IR&D/N/2026-27/2",
            ProjectReference = "Unlinked project",
            Category = ArppCategory.New,
            IpaCost = 2_000_000m,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });

        var addendum = CreateIssue(2026, 1, ArppIssueKind.Addendum, "Addendum No. 1");
        addendum.Entries.Add(CreateEntry(1, "7", ArppCategory.CarryForward, 12_000_000m));
        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var register = await new ArppReadService(db).GetRegisterAsync(null, null);

        Assert.Equal(12_000_000m, register.ApprovedLinkedIpaCost);
        Assert.Equal(0m, register.DelistedLinkedIpaCost);
        Assert.Equal(1, register.ApprovedLinkedProjects);
        Assert.Equal(0, register.DelistedLinkedProjects);
        Assert.Equal(12_000_000m, register.AuthoritativeLinkedIpaCost);
        Assert.Equal(1, register.LinkedEntries);
        Assert.Equal(2_000_000m, register.UnlinkedDocumentRowValue);
        Assert.Equal(24_000_000m, register.FinancialYears.Single().Issues.Sum(item => item.TotalIpaCost));
        Assert.Equal(12_000_000m, register.FinancialYears.Single().ApprovedLinkedIpaCost);
        Assert.Equal(0m, register.FinancialYears.Single().DelistedLinkedIpaCost);
    }


    [Fact]
    public async Task GetRegisterAsync_SeparatesApprovedAndDelistedLatestPositions()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Project moved to Delisted", CreatedByUserId = "seed" },
            new Project { Id = 2, Name = "Approved carry-forward project", CreatedByUserId = "seed" });

        var original = CreateIssue(2026, 0, ArppIssueKind.Original, "ARPP 2026-27");
        original.Entries.Add(CreateEntry(1, "1", ArppCategory.New, 10_000_000m));
        original.Entries.Add(CreateEntry(2, "2", ArppCategory.CarryForward, 5_000_000m));

        var addendum = CreateIssue(2026, 1, ArppIssueKind.Addendum, "Addendum No. 1");
        addendum.Entries.Add(CreateEntry(1, "7", ArppCategory.Delisted, 12_000_000m));

        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var register = await new ArppReadService(db).GetRegisterAsync(null, null);
        var financialYear = register.FinancialYears.Single();
        var originalItem = financialYear.Issues.Single(item => item.IssueSequence == 0);
        var addendumItem = financialYear.Issues.Single(item => item.IssueSequence == 1);

        Assert.Equal(5_000_000m, register.ApprovedLinkedIpaCost);
        Assert.Equal(12_000_000m, register.DelistedLinkedIpaCost);
        Assert.Equal(1, register.ApprovedLinkedProjects);
        Assert.Equal(1, register.DelistedLinkedProjects);
        Assert.Equal(17_000_000m, register.AuthoritativeLinkedIpaCost);

        Assert.Equal(5_000_000m, financialYear.ApprovedLinkedIpaCost);
        Assert.Equal(12_000_000m, financialYear.DelistedLinkedIpaCost);
        Assert.Equal(1, financialYear.ApprovedLinkedProjectCount);
        Assert.Equal(1, financialYear.DelistedLinkedProjectCount);

        Assert.Equal(15_000_000m, originalItem.ApprovedRowValue);
        Assert.Equal(0m, originalItem.DelistedRowValue);
        Assert.Equal(0m, addendumItem.ApprovedRowValue);
        Assert.Equal(12_000_000m, addendumItem.DelistedRowValue);
    }


    [Fact]
    public async Task GetRegisterAsync_SearchingHistoricalSerial_UsesLatestApplicablePosition()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project moved to Delisted",
            CreatedByUserId = "seed"
        });

        var original = CreateIssue(2026, 0, ArppIssueKind.Original, "ARPP 2026-27");
        original.Entries.Add(CreateEntry(1, "OLD-12", ArppCategory.New, 10_000_000m));

        var addendum = CreateIssue(2026, 1, ArppIssueKind.Addendum, "Addendum No. 1");
        addendum.Entries.Add(CreateEntry(1, "NEW-7", ArppCategory.Delisted, 12_000_000m));

        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var register = await new ArppReadService(db).GetRegisterAsync(null, "OLD-12");

        Assert.Single(register.FinancialYears);
        Assert.Single(register.FinancialYears.Single().Issues);
        Assert.Equal(original.Id, register.FinancialYears.Single().Issues.Single().Id);
        Assert.Equal(0m, register.ApprovedLinkedIpaCost);
        Assert.Equal(12_000_000m, register.DelistedLinkedIpaCost);
        Assert.Equal(0, register.ApprovedLinkedProjects);
        Assert.Equal(1, register.DelistedLinkedProjects);
        Assert.Equal(0m, register.FinancialYears.Single().ApprovedLinkedIpaCost);
        Assert.Equal(12_000_000m, register.FinancialYears.Single().DelistedLinkedIpaCost);
    }

    [Fact]
    public async Task GetIssueAsync_ResolvesVerifierDisplayName()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "verifier-1",
            Rank = "Col",
            FullName = "Verifier One",
            UserName = "verifier.one"
        });

        var issue = CreateIssue(2026, 0, ArppIssueKind.Original, "ARPP 2026-27");
        issue.IsVerified = true;
        issue.VerifiedAtUtc = new DateTimeOffset(2026, 7, 26, 16, 47, 0, TimeSpan.Zero);
        issue.VerifiedByUserId = "verifier-1";
        issue.VerificationNote = "Checked against the issued HQ PDF.";
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var details = await new ArppReadService(db).GetIssueAsync(issue.Id);

        Assert.NotNull(details);
        Assert.Equal("verifier-1", details!.VerifiedByUserId);
        Assert.Equal("Col Verifier One", details.VerifiedByDisplayName);
        Assert.Equal("Checked against the issued HQ PDF.", details.VerificationNote);
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
            SerialNumber = category == ArppCategory.Delisted ? null : serial,
            PppNumber = category == ArppCategory.Delisted ? null : $"ARPP/IR&D/2026-27/{serial}",
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
