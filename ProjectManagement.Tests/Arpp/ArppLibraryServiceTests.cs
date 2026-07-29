using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppLibraryServiceTests
{
    [Fact]
    public async Task CurrentPosition_UsesLatestPublishedRow_AndIgnoresWorkingChanges()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Astra",
            CreatedByUserId = "seed"
        });

        var original = BuildIssue(1, 2026, ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 2, 25));
        original.PublishedSnapshot = BuildPublished(
            original,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 101,
                SortOrder = 1,
                SerialNumber = "10",
                PppNumber = "ARPP/IR&D/2026-27/10",
                ProjectReference = "Project Astra",
                ProjectId = 1,
                Category = ArppCategory.New,
                IpaCost = 10_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });

        var addendum = BuildIssue(2, 2026, ArppIssueKind.Addendum, 1, "Addendum 1", new DateOnly(2026, 6, 15));
        addendum.PublishedSnapshot = BuildPublished(
            addendum,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 201,
                SortOrder = 1,
                SerialNumber = null,
                PppNumber = null,
                ProjectReference = "Project Astra revised",
                ProjectId = 1,
                Category = ArppCategory.Delisted,
                IpaCost = 12_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });

        // A working correction exists in the management tables but is not published.
        addendum.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "99",
            PppNumber = "ARPP/IR&D/N/2026-27/99",
            ProjectReference = "Unverified working value",
            ProjectId = 1,
            Category = ArppCategory.New,
            IpaCost = 99_000_000m,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        });

        db.ArppIssues.AddRange(original, addendum);
        await db.SaveChangesAsync();

        var service = new ArppLibraryService(db, new FakeStorage());
        var current = await service.GetCurrentPositionAsync(2026, query: null);

        Assert.NotNull(current);
        Assert.Empty(current!.ApprovedRows);
        var delisted = Assert.Single(current.DelistedRows);
        Assert.Equal(12_000_000m, delisted.IpaCost);
        Assert.Equal("Project Astra revised", delisted.ProjectReference);
        Assert.Equal(2, delisted.SourceIssueId);
        Assert.Null(delisted.SerialNumber);
        Assert.Null(delisted.PppNumber);
        Assert.Equal(0, current.TotalUnlinkedDocumentRows);

        var history = await service.GetProjectHistoryAsync(1);
        Assert.NotNull(history);
        Assert.Equal(2, history!.Rows.Count);
        Assert.Equal("Project Astra revised", history.Rows[0].ProjectReference);
        Assert.Equal(12_000_000m, history.Rows[0].IpaCost);
        Assert.DoesNotContain(history.Rows, row => row.ProjectReference == "Unverified working value");
    }

    [Fact]
    public async Task CurrentPosition_ExcludesAndExposesUnlinkedPublishedRows()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Linked project",
            CreatedByUserId = "seed"
        });

        var issue = BuildIssue(1, 2026, ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 2, 25));
        issue.PublishedSnapshot = BuildPublished(
            issue,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 401,
                SortOrder = 1,
                SerialNumber = "1",
                PppNumber = "ARPP/IR&D/2026-27/1",
                ProjectReference = "Linked project",
                ProjectId = 1,
                Category = ArppCategory.New,
                IpaCost = 10_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            },
            new ArppPublishedEntry
            {
                SourceEntryId = 402,
                SortOrder = 2,
                SerialNumber = "2",
                PppNumber = "ARPP/IR&D/2026-27/2",
                ProjectReference = "Issued project not yet created in PRISM",
                ProjectId = null,
                Category = ArppCategory.CarryForward,
                IpaCost = 5_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });

        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = new ArppLibraryService(db, new FakeStorage());
        var current = await service.GetCurrentPositionAsync(2026, query: null);

        Assert.NotNull(current);
        Assert.Equal(10_000_000m, current!.ApprovedIpaValue);
        Assert.Equal(1, current.TotalUnlinkedDocumentRows);
        var unlinked = Assert.Single(current.UnlinkedRows);
        Assert.Equal("Issued project not yet created in PRISM", unlinked.ProjectReference);
        Assert.Equal(5_000_000m, unlinked.IpaCost);

        var filtered = await service.GetCurrentPositionAsync(2026, "no match");
        Assert.NotNull(filtered);
        Assert.Empty(filtered!.UnlinkedRows);
        Assert.Equal(1, filtered.TotalUnlinkedDocumentRows);
    }

    [Fact]
    public async Task NavigationAndDocument_ExposeOnlyPublishedSnapshots()
    {
        await using var db = CreateContext();
        var publishedIssue = BuildIssue(1, 2026, ArppIssueKind.Original, 0, "Published ARPP", new DateOnly(2026, 2, 25));
        publishedIssue.PublishedSnapshot = BuildPublished(
            publishedIssue,
            revision: 2,
            new ArppPublishedEntry
            {
                SourceEntryId = 301,
                SortOrder = 1,
                SerialNumber = "1",
                PppNumber = "ARPP/IR&D/2026-27/1",
                ProjectReference = "Published project",
                Category = ArppCategory.New,
                IpaCost = 5_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });
        var draftIssue = BuildIssue(2, 2025, ArppIssueKind.Original, 0, "Draft only", new DateOnly(2025, 2, 25));
        db.ArppIssues.AddRange(publishedIssue, draftIssue);
        await db.SaveChangesAsync();

        var service = new ArppLibraryService(db, new FakeStorage());
        var navigation = await service.GetNavigationAsync(query: null);
        var document = await service.GetDocumentAsync(1);

        Assert.Equal(1, navigation.PublishedDocumentCount);
        Assert.Single(navigation.FinancialYears);
        Assert.NotNull(document);
        Assert.Equal(2, document!.RevisionNumber);
        Assert.Equal("Published project", Assert.Single(document.Rows).ProjectReference);
        Assert.Null(await service.GetDocumentAsync(2));
    }


    [Theory]
    [InlineData("GOC-In-ARTRAC")]
    [InlineData("Capability Development Fund")]
    [InlineData("9.2")]
    [InlineData("CF")]
    [InlineData("carry forward")]
    [InlineData("ARPP/IR&D/2026-27/27")]
    public async Task LibrarySearch_UsesSameControlledFieldsAcrossNavigationAndCurrentPosition(string query)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 7,
            Name = "Project Trident",
            CreatedByUserId = "seed"
        });

        var issue = BuildIssue(
            7,
            2026,
            ArppIssueKind.Original,
            0,
            "ARPP 2026-27",
            new DateOnly(2026, 2, 25));

        issue.PublishedSnapshot = BuildPublished(
            issue,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 701,
                SortOrder = 1,
                SerialNumber = "27",
                PppNumber = "ARPP/IR&D/2026-27/27",
                ProjectReference = "Project Trident as issued",
                ProjectId = 7,
                Category = ArppCategory.CarryForward,
                IpaCost = 25_000_000m,
                Cfa = "GOC-In-ARTRAC",
                Fund = "Capability Development Fund",
                DfpdsSchedule = "9.2"
            });

        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = new ArppLibraryService(db, new FakeStorage());

        var navigation = await service.GetNavigationAsync(query);
        var current = await service.GetCurrentPositionAsync(2026, query);

        Assert.Equal(1, navigation.PublishedDocumentCount);
        Assert.NotNull(current);
        var row = Assert.Single(current!.ApprovedRows);
        Assert.Equal(7, row.ProjectId);
        Assert.Equal(ArppCategory.CarryForward, row.Category);
        Assert.Equal("27", row.SerialNumber);
        Assert.Equal("ARPP/IR&D/2026-27/27", row.PppNumber);
    }

    [Fact]
    public async Task ProjectHistory_OrdersLatestFinancialYearAndLatestAddendumFirst()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 9,
            Name = "Project Orion",
            CreatedByUserId = "seed"
        });

        var older = BuildIssue(11, 2025, ArppIssueKind.Addendum, 4, "Older FY addendum", new DateOnly(2026, 1, 20));
        older.PublishedSnapshot = BuildPublished(
            older,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 1101,
                SortOrder = 1,
                SerialNumber = "91",
                PppNumber = "ARPP/IR&D/2026-27/91",
                ProjectReference = "Project Orion older",
                ProjectId = 9,
                Category = ArppCategory.CarryForward,
                IpaCost = 11_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });

        var original = BuildIssue(12, 2026, ArppIssueKind.Original, 0, "Current FY original", new DateOnly(2026, 2, 25));
        original.PublishedSnapshot = BuildPublished(
            original,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 1201,
                SortOrder = 1,
                SerialNumber = "12",
                PppNumber = "ARPP/IR&D/2026-27/12",
                ProjectReference = "Project Orion current",
                ProjectId = 9,
                Category = ArppCategory.New,
                IpaCost = 12_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });

        var latest = BuildIssue(13, 2026, ArppIssueKind.Addendum, 1, "Current FY addendum", new DateOnly(2026, 5, 10));
        latest.PublishedSnapshot = BuildPublished(
            latest,
            revision: 1,
            new ArppPublishedEntry
            {
                SourceEntryId = 1301,
                SortOrder = 1,
                SerialNumber = "3",
                PppNumber = "ARPP/IR&D/2026-27/3",
                ProjectReference = "Project Orion latest",
                ProjectId = 9,
                Category = ArppCategory.CommittedLiability,
                IpaCost = 13_000_000m,
                Cfa = "Comdt SDD",
                Fund = "IR&D",
                DfpdsSchedule = "9.3"
            });

        db.ArppIssues.AddRange(older, original, latest);
        await db.SaveChangesAsync();

        var service = new ArppLibraryService(db, new FakeStorage());
        var history = await service.GetProjectHistoryAsync(9);

        Assert.NotNull(history);
        Assert.Equal(3, history!.Rows.Count);
        Assert.Equal(13, history.Rows[0].SourceIssueId);
        Assert.Equal(12, history.Rows[1].SourceIssueId);
        Assert.Equal(11, history.Rows[2].SourceIssueId);
    }

    private static ArppIssue BuildIssue(
        long id,
        int financialYearStart,
        ArppIssueKind kind,
        int sequence,
        string name,
        DateOnly issueDate)
        => new()
        {
            Id = id,
            FinancialYearStart = financialYearStart,
            Kind = kind,
            IssueSequence = sequence,
            Name = name,
            IssueDate = issueDate,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

    private static ArppPublishedIssue BuildPublished(
        ArppIssue issue,
        int revision,
        params ArppPublishedEntry[] entries)
    {
        var snapshot = new ArppPublishedIssue
        {
            ArppIssueId = issue.Id,
            RevisionNumber = revision,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issue.IssueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = $"published/{issue.Id}.pdf",
            AttachmentOriginalFileName = "ARPP.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64)
        };

        foreach (var entry in entries)
        {
            entry.ArppIssueId = issue.Id;
            snapshot.Entries.Add(entry);
        }

        return snapshot;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeStorage : IArppAttachmentStorage
    {
        public Task<ArppStoredAttachment> SaveAsync(
            long issueId,
            string originalFileName,
            string contentType,
            long declaredLength,
            Stream content,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream("%PDF-test"u8.ToArray()));

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
