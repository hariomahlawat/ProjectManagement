using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

/// <summary>
/// Amendment letters are intermediate HQ letters that are neither the original ARPP nor a
/// numbered addendum. They are numbered separately from addenda within a financial year,
/// may carry rows, and take part in the current position by issue date.
/// </summary>
public sealed class ArppAmendmentLetterTests
{
    [Fact]
    public void IssueLabel_NamesEachKind()
    {
        Assert.Equal("Original ARPP", ArppDisplayNames.IssueLabel(ArppIssueKind.Original, 0));
        Assert.Equal("Addendum No. 2", ArppDisplayNames.IssueLabel(ArppIssueKind.Addendum, 2));
        Assert.Equal("Amendment No. 1", ArppDisplayNames.IssueLabel(ArppIssueKind.Amendment, 1));
        Assert.Equal("Amendment letter", ArppDisplayNames.For(ArppIssueKind.Amendment));
        Assert.Equal("Amendment_1", ArppDisplayNames.IssueFileLabel(ArppIssueKind.Amendment, 1));

        // Contracts that only carry a sequence still read 0 as the original.
        Assert.Equal("Original ARPP", ArppDisplayNames.IssueLabel(null, 0));
        Assert.Equal("Addendum No. 3", ArppDisplayNames.IssueLabel(null, 3));
    }

    [Fact]
    public async Task CreateIssue_NumbersAmendmentsSeparatelyFromAddenda()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var original = await service.CreateIssueAsync(Create(ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 4, 10)));
        var addendum = await service.CreateIssueAsync(Create(ArppIssueKind.Addendum, 1, "Addendum 1", new DateOnly(2026, 6, 1)));
        var amendment = await service.CreateIssueAsync(Create(ArppIssueKind.Amendment, 1, "Amendment letter 1", new DateOnly(2026, 7, 1)));

        Assert.True(original.Success, original.Message);
        Assert.True(addendum.Success, addendum.Message);
        Assert.True(amendment.Success, amendment.Message);
        Assert.Equal(3, await db.ArppIssues.CountAsync());

        var duplicate = await service.CreateIssueAsync(Create(ArppIssueKind.Amendment, 1, "Amendment letter 1 again", new DateOnly(2026, 8, 1)));
        Assert.False(duplicate.Success);
        Assert.Contains("amendment letter already uses this number", duplicate.FieldErrors["IssueSequence"].Single(), StringComparison.OrdinalIgnoreCase);

        var unnumbered = await service.CreateIssueAsync(Create(ArppIssueKind.Amendment, 0, "Amendment without a number", new DateOnly(2026, 8, 1)));
        Assert.False(unnumbered.Success);
        Assert.Contains("amendment letter number", unnumbered.FieldErrors["IssueSequence"].Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateIssue_ChecksAmendmentChronologyAgainstOtherAmendmentsOnly()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        await service.CreateIssueAsync(Create(ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 4, 10)));
        await service.CreateIssueAsync(Create(ArppIssueKind.Addendum, 1, "Addendum 1", new DateOnly(2026, 9, 1)));

        // An amendment issued before Addendum 1 is normal: the two series are independent.
        var amendment = await service.CreateIssueAsync(Create(ArppIssueKind.Amendment, 1, "Amendment letter 1", new DateOnly(2026, 6, 1)));
        Assert.True(amendment.Success, amendment.Message);
        Assert.DoesNotContain(amendment.Warnings, warning => warning.Contains("chronology", StringComparison.OrdinalIgnoreCase));

        // An amendment dated before the original is still worth a warning.
        var early = await service.CreateIssueAsync(Create(ArppIssueKind.Amendment, 2, "Amendment letter 2", new DateOnly(2026, 3, 1)));
        Assert.True(early.Success, early.Message);
        Assert.Contains(early.Warnings, warning => warning.Contains("precedes the original ARPP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuggestedSequence_IsPerKind()
    {
        await using var db = CreateContext();
        db.ArppIssues.AddRange(
            BuildIssue(1, 2026, ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 4, 10)),
            BuildIssue(2, 2026, ArppIssueKind.Addendum, 1, "Addendum 1", new DateOnly(2026, 6, 1)),
            BuildIssue(3, 2026, ArppIssueKind.Addendum, 2, "Addendum 2", new DateOnly(2026, 8, 1)),
            BuildIssue(4, 2026, ArppIssueKind.Amendment, 1, "Amendment 1", new DateOnly(2026, 7, 1)));
        await db.SaveChangesAsync();

        var service = new ArppReadService(db);

        Assert.Equal(0, await service.GetSuggestedIssueSequenceAsync(2026, ArppIssueKind.Original));
        Assert.Equal(3, await service.GetSuggestedIssueSequenceAsync(2026, ArppIssueKind.Addendum));
        Assert.Equal(2, await service.GetSuggestedIssueSequenceAsync(2026, ArppIssueKind.Amendment));
        Assert.Equal(1, await service.GetSuggestedIssueSequenceAsync(2027, ArppIssueKind.Amendment));
    }

    [Fact]
    public async Task CurrentPosition_FollowsIssueDate_WhenAnAmendmentIsLaterThanAHigherNumberedAddendum()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project Astra", CreatedByUserId = "seed" });

        var original = BuildIssue(1, 2026, ArppIssueKind.Original, 0, "ARPP 2026-27", new DateOnly(2026, 4, 10));
        original.PublishedSnapshot = BuildPublished(original, Row(101, 1, ArppCategory.New, 10_000_000m));

        // Addendum 2 carries a higher sequence number but was issued before Amendment 1.
        var addendum = BuildIssue(2, 2026, ArppIssueKind.Addendum, 2, "Addendum 2", new DateOnly(2026, 6, 15));
        addendum.PublishedSnapshot = BuildPublished(addendum, Row(201, 1, ArppCategory.CommittedLiability, 12_000_000m));

        var amendment = BuildIssue(3, 2026, ArppIssueKind.Amendment, 1, "Amendment letter 1", new DateOnly(2026, 9, 20));
        amendment.PublishedSnapshot = BuildPublished(amendment, Row(301, 1, ArppCategory.Delisted, 12_000_000m));

        db.ArppIssues.AddRange(original, addendum, amendment);
        await db.SaveChangesAsync();

        var library = new ArppLibraryService(db, new FakeStorage());
        var position = await library.GetCurrentPositionAsync(2026, query: null);

        Assert.NotNull(position);
        Assert.Empty(position!.ApprovedRows);
        var delisted = Assert.Single(position.DelistedRows);
        Assert.Equal(3, delisted.SourceIssueId);
        Assert.Equal(ArppIssueKind.Amendment, delisted.SourceKind);

        var history = await library.GetProjectHistoryAsync(1);
        Assert.NotNull(history);
        Assert.Equal([3L, 2L, 1L], history!.Rows.Select(row => row.SourceIssueId).ToArray());

        // The published library lists letters in the order they were issued.
        var navigation = await library.GetNavigationAsync(query: null);
        var year = Assert.Single(navigation.FinancialYears);
        Assert.Equal([1L, 2L, 3L], year.Documents.Select(document => document.IssueId).ToArray());

        var resolver = new AuthoritativeIpaPositionResolver(db);
        var ipa = await resolver.ResolveAsync(1);
        Assert.NotNull(ipa);
        Assert.Equal(3, ipa!.IssueId);
        Assert.Equal(ArppIssueKind.Amendment, ipa.IssueKind);
        Assert.True(ipa.IsDelisted);
    }

    private static ArppIssueCreateCommand Create(ArppIssueKind kind, int sequence, string name, DateOnly issueDate)
        => new(2026, kind, sequence, name, issueDate, "user-1", "User One");

    private static ArppPublishedEntry Row(long sourceEntryId, int sortOrder, ArppCategory category, decimal ipaCost)
        => new()
        {
            SourceEntryId = sourceEntryId,
            SortOrder = sortOrder,
            SerialNumber = category == ArppCategory.Delisted ? null : sortOrder.ToString(),
            PppNumber = category == ArppCategory.Delisted ? null : $"ARPP/IR&D/N/2026-27/{sortOrder}",
            ProjectReference = "Project Astra",
            ProjectId = 1,
            Category = category,
            IpaCost = ipaCost,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3"
        };

    private static ArppIssue BuildIssue(long id, int financialYearStart, ArppIssueKind kind, int sequence, string name, DateOnly issueDate)
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

    private static ArppPublishedIssue BuildPublished(ArppIssue issue, params ArppPublishedEntry[] entries)
    {
        var snapshot = new ArppPublishedIssue
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

    private static ArppCommandService CreateService(ApplicationDbContext db)
        => new(db, new FixedClock(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)), new FakeAuditService());

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
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class FakeStorage : IArppAttachmentStorage
    {
        public Task<ArppStoredAttachment> SaveAsync(long issueId, string originalFileName, string contentType, long declaredLength, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream("%PDF-test"u8.ToArray()));

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
