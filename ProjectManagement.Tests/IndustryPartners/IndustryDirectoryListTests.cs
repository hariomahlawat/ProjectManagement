using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Pages.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Directory list redesign — initials, sorting, paging and Excel export
public sealed class IndustryDirectoryListTests
{
    [Theory]
    [InlineData("Aravalli Defence Systems Pvt Ltd", "AD")]
    [InlineData("M/s Shakti Electronic Warfare Solutions", "SE")]
    [InlineData("M/s. Garuda Aero", "GA")]
    [InlineData("Messrs Kaveri Sensor Networks", "KS")]
    [InlineData("3M India", "3I")]
    [InlineData("Eastern Ballistics & Composites", "EB")]
    [InlineData("Goa", "G")]
    [InlineData("   ", "?")]
    public void Avatar_Initials_UseTheFirstTwoWordsAfterAnyHonorific(string name, string expected)
    {
        Assert.Equal(expected, IndustryPartnerAvatar.Initials(name));
    }

    [Fact]
    public void Avatar_Tone_IsStableAndIgnoresCaseAndSurroundingSpaces()
    {
        var tone = IndustryPartnerAvatar.Tone("Aravalli Defence Systems Pvt Ltd");

        Assert.InRange(tone, 0, IndustryPartnerAvatar.ToneCount - 1);
        Assert.Equal(tone, IndustryPartnerAvatar.Tone("  aravalli defence systems pvt ltd "));
        Assert.Equal($"ip-tone-{tone}", IndustryPartnerAvatar.ToneClass("Aravalli Defence Systems Pvt Ltd"));

        // Different organisations spread across tones rather than all sharing one.
        var tones = Enumerable.Range(0, 40).Select(i => IndustryPartnerAvatar.Tone($"Organisation {i}")).Distinct().Count();
        Assert.True(tones >= 5, $"Expected a spread of tones, got {tones}.");
    }

    [Fact]
    public async Task Search_RecentlyUpdated_ListsTheLatestChangeFirst()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var older = await db.AddPartnerAsync("Alpha Older");
        var newer = await db.AddPartnerAsync("Zeta Newer");
        await using (var context = db.NewContext())
        {
            var now = DateTimeOffset.UtcNow;
            (await context.IndustryPartners.SingleAsync(p => p.Id == older)).UpdatedUtc = now.AddDays(-10);
            (await context.IndustryPartners.SingleAsync(p => p.Id == newer)).UpdatedUtc = now.AddDays(-1);
            await context.SaveChangesAsync();
        }

        var service = new IndustryPartnerService(db.NewContext());
        var byName = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.All, 1, 25, IndustryPartnerDirectorySort.Name);
        var byUpdated = await service.SearchAsync(null, IndustryPartnerDirectoryFilter.All, 1, 25, IndustryPartnerDirectorySort.RecentlyUpdated);

        Assert.Equal(new[] { "Alpha Older", "Zeta Newer" }, byName.Items.Select(item => item.Name));
        Assert.Equal(new[] { "Zeta Newer", "Alpha Older" }, byUpdated.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Export_MatchesTheListAndCarriesEveryContactInDirectoryOrder()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var live = await db.AddProjectAsync("Live project");
        var deleted = await db.AddProjectAsync("Deleted project", isDeleted: true);
        var linked = await db.AddPartnerAsync("Linked Org");
        await db.AddPartnerAsync("Contact Only Org");
        await db.AddLinkAsync(linked, live);
        await db.AddLinkAsync(linked, deleted);
        var service = new IndustryPartnerService(db.NewContext());
        var author = User("author");
        await service.AddContactAsync(linked, new ContactRequest("First Person", "111", null), author);
        await service.AddContactAsync(linked, new ContactRequest(null, null, "desk@linked.example"), author);

        var all = await service.ExportDirectoryAsync(null, IndustryPartnerDirectoryFilter.All, IndustryPartnerDirectorySort.Name);
        var jdpOnly = await service.ExportDirectoryAsync(null, IndustryPartnerDirectoryFilter.JdpAssociated, IndustryPartnerDirectorySort.Name);
        var searched = await service.ExportDirectoryAsync("contact only", IndustryPartnerDirectoryFilter.All, IndustryPartnerDirectorySort.Name);

        Assert.Equal(new[] { "Contact Only Org", "Linked Org" }, all.Select(row => row.Name));
        var row = Assert.Single(jdpOnly);
        Assert.Equal(1, row.ProjectCount);
        Assert.Equal(1, row.ActiveProjectCount);
        Assert.Equal("Current JDP", row.StatusLabel);
        Assert.Equal(new[] { "First Person", null }, row.Contacts.Select(contact => contact.Name));
        Assert.Equal("First Person", row.PrimaryContact!.Name);
        Assert.Equal("Contact Only Org", Assert.Single(searched).Name);
    }

    [Fact]
    public void ExcelWorkbook_WritesTextExactly_AndListsEveryContact()
    {
        var rows = new[]
        {
            new IndustryPartnerExportRow(1, "=HYPERLINK(\"x\")", "Pune", "Notes", 2, 1, DateTimeOffset.UtcNow,
                new[]
                {
                    new IndustryPartnerExportContact("Ms Rao", "+91 98765 43210", "rao@example.test"),
                    new IndustryPartnerExportContact(null, "020 1234 5678", null)
                }),
            new IndustryPartnerExportRow(2, "Quiet Org", null, null, 0, 0, DateTimeOffset.UtcNow,
                Array.Empty<IndustryPartnerExportContact>())
        };

        var bytes = IndustryPartnerDirectoryExcel.Build(rows, "All organisations", DateTimeOffset.UtcNow);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var organisations = workbook.Worksheet("Organisations");
        Assert.Equal("Organisation", organisations.Cell(1, 1).GetString());
        Assert.Equal("Primary contact", organisations.Cell(1, 3).GetString());

        var formulaLike = organisations.Cell(2, 1);
        Assert.False(formulaLike.HasFormula);
        Assert.Equal("=HYPERLINK(\"x\")", formulaLike.GetString());
        Assert.Equal("+91 98765 43210", organisations.Cell(2, 4).GetString());
        Assert.Equal(XLDataType.Text, organisations.Cell(2, 4).DataType);
        Assert.Equal("Current JDP", organisations.Cell(2, 7).GetString());
        Assert.Equal("Contact only", organisations.Cell(3, 7).GetString());

        var contacts = workbook.Worksheet("Contacts");
        Assert.Equal("Ms Rao", contacts.Cell(2, 2).GetString());
        Assert.Equal("Yes", contacts.Cell(2, 5).GetString());
        Assert.Equal("General contact", contacts.Cell(3, 2).GetString());
        Assert.Equal(string.Empty, contacts.Cell(3, 5).GetString());
        Assert.True(contacts.Cell(4, 1).IsEmpty());

        Assert.Equal("All organisations", workbook.Worksheet("About").Cell(2, 2).GetString());
    }

    [Fact]
    public async Task ExportHandler_ReturnsAnExcelFileForTheCurrentFilter()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var project = await db.AddProjectAsync("Project");
        var linked = await db.AddPartnerAsync("Linked Org");
        await db.AddPartnerAsync("Contact Only Org");
        await db.AddLinkAsync(linked, project);
        var page = IndustryPartnerJdpAuthorizationTests.CreatePage(new IndustryPartnerService(db.NewContext()), User("viewer"));
        page.Filter = "associated";
        page.Sort = "updated";

        var result = Assert.IsType<FileContentResult>(await page.OnGetExportAsync(CancellationToken.None));

        Assert.Equal(IndustryPartnerDirectoryExcel.ContentType, result.ContentType);
        Assert.StartsWith("prism-industry-directory-", result.FileDownloadName);
        Assert.EndsWith(".xlsx", result.FileDownloadName);
        using var workbook = new XLWorkbook(new MemoryStream(result.FileContents));
        var sheet = workbook.Worksheet("Organisations");
        Assert.Equal("Linked Org", sheet.Cell(2, 1).GetString());
        Assert.True(sheet.Cell(3, 1).IsEmpty());
        Assert.Contains("All JDPs", workbook.Worksheet("About").Cell(2, 2).GetString());
        Assert.Contains("Recently updated", workbook.Worksheet("About").Cell(2, 2).GetString());
    }

    [Theory]
    [InlineData(1, new[] { 1, 2, 3, 4, 5 })]
    [InlineData(4, new[] { 2, 3, 4, 5, 6 })]
    [InlineData(7, new[] { 3, 4, 5, 6, 7 })]
    public async Task PageWindow_ShowsUpToFivePagesAroundTheCurrentOne(int current, int[] expected)
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        await using (var seed = db.NewContext())
        {
            for (var i = 0; i < 160; i++)
            {
                var name = $"Organisation {i:D3}";
                seed.IndustryPartners.Add(new IndustryPartner { Name = name, NormalizedName = name.ToLowerInvariant(), CreatedByUserId = "seed" });
            }

            await seed.SaveChangesAsync();
        }

        // 160 organisations at 25 per page is 7 pages.
        var page = IndustryPartnerJdpAuthorizationTests.CreatePage(new IndustryPartnerService(db.NewContext()), User("viewer"));
        page.PageNumber = current;
        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(7, page.SearchResult.TotalPages);
        Assert.Equal(expected, page.PageWindow());
    }

    [Fact]
    public async Task UnknownSortValue_FallsBackToName()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var page = IndustryPartnerJdpAuthorizationTests.CreatePage(new IndustryPartnerService(db.NewContext()), User("viewer"));
        page.Sort = "nonsense";

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(IndustryPartnerDirectorySort.Name, page.SelectedSort);
        Assert.Equal("name", page.Sort);
    }
}
