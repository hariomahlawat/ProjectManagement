using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppExcelWorkbookBuilderTests
{
    [Fact]
    public async Task Build_PreservesNumericIpaCost_AndDelistedCategory()
    {
        await using var db = CreateContext();
        var issue = new ArppIssue
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Addendum,
            IssueSequence = 2,
            Name = "ARPP Addendum No. 2",
            IssueDate = new DateOnly(2026, 7, 20),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };
        issue.Entries.Add(new ArppEntry
        {
            SortOrder = 1,
            SerialNumber = "17",
            ProjectReference = "Project Astra",
            Category = ArppCategory.Delisted,
            IpaCost = 47_500_000m,
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

        var details = await new ArppReadService(db).GetIssueAsync(issue.Id);
        Assert.NotNull(details);

        var bytes = new ArppExcelWorkbookBuilder().Build(
            details!,
            new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("ARPP");
        Assert.Equal("Delisted", sheet.Cell(9, 6).GetString());
        Assert.Equal(47_500_000m, sheet.Cell(9, 7).GetValue<decimal>());
        Assert.Contains("₹", sheet.Cell(9, 7).Style.NumberFormat.Format);
        Assert.Contains("03:30 PM IST", sheet.Cell(3, 1).GetString());
        Assert.False(sheet.Cell(3, 1).GetString().Contains(" UTC", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PublishedReaderMode_OmitsAuditAndChecksumMetadata()
    {
        var publishedAt = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var details = new ArppIssueDetails(
            1,
            2026,
            ArppIssueKind.Original,
            0,
            "ARPP 2026-27",
            new DateOnly(2026, 2, 26),
            string.Empty,
            new[]
            {
                new ArppEntryDetails(
                    10,
                    1,
                    "12",
                    "Project reference as issued",
                    7,
                    "Linked PRISM name",
                    "CF/7",
                    "Ongoing",
                    ArppCategory.New,
                    10_000_000m,
                    1,
                    "Comdt SDD",
                    1,
                    "IR&D",
                    1,
                    "9.3",
                    string.Empty)
            },
            10_000_000m,
            Enum.GetValues<ArppCategory>().ToDictionary(
                category => category,
                category => new ArppCategorySummary(
                    category,
                    category == ArppCategory.New ? 1 : 0,
                    category == ArppCategory.New ? 10_000_000m : 0m)),
            1,
            0,
            publishedAt,
            publishedAt,
            new ArppAttachmentDetails(
                0,
                "Issued-ARPP.pdf",
                "application/pdf",
                100,
                new string('a', 64),
                "verifier",
                publishedAt,
                string.Empty),
            true,
            publishedAt,
            "verifier",
            "Colonel Verifier",
            "Checked against source");

        var bytes = new ArppExcelWorkbookBuilder().Build(
            details,
            publishedAt,
            includeRecordControlMetadata: false,
            includePrismLinkageColumns: false);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var metadata = workbook.Worksheet("ARPP").Cell(4, 1).GetString();

        Assert.Contains("Published structured representation", metadata);
        Assert.Contains("Issued-ARPP.pdf", metadata);
        Assert.DoesNotContain("SHA-256", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verified", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Colonel Verifier", metadata, StringComparison.OrdinalIgnoreCase);

        var sheet = workbook.Worksheet("ARPP");
        Assert.Equal("Project reference as issued", sheet.Cell(8, 3).GetString());
        Assert.Equal("Category", sheet.Cell(8, 4).GetString());
        Assert.Equal("DFPDS schedule", sheet.Cell(8, 8).GetString());
        Assert.Equal(string.Empty, sheet.Cell(8, 9).GetString());
        Assert.DoesNotContain(
            sheet.Row(8).CellsUsed().Select(cell => cell.GetString()),
            heading => heading.Contains("PRISM", StringComparison.OrdinalIgnoreCase) ||
                       heading.Contains("Link status", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Project reference as issued", sheet.Cell(9, 3).GetString());
        Assert.Equal("New", sheet.Cell(9, 4).GetString());
        Assert.Equal(10_000_000m, sheet.Cell(9, 5).GetValue<decimal>());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
