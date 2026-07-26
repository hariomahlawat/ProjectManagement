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
        Assert.Equal(47_500_000m, sheet.Cell(9, 7).GetDecimal());
        Assert.Contains("₹", sheet.Cell(9, 7).Style.NumberFormat.Format);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
