using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Pages.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Choosing the primary contact, contact cards, and the full organisation page
public sealed class IndustryPartnerPrimaryContactAndPageTests
{
    private static async Task<(IndustryPartnerTestDb Db, int PartnerId, int First, int Second, int Third)> SeedAsync()
    {
        var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Aravalli Defence", createdByUserId: "owner");
        var service = new IndustryPartnerService(db.NewContext());
        var owner = User("owner");
        var first = await service.AddContactAsync(partnerId, new ContactRequest("Business Development Office", "0124 455 0100", null), owner);
        var second = await service.AddContactAsync(partnerId, new ContactRequest("Ms Neha Kapoor", "+91 98101 77421", "neha@aravalli.example"), owner);
        var third = await service.AddContactAsync(partnerId, new ContactRequest("Col (Retd) Vikram Sethi, Director BD", "+91 98110 24567", "vikram@aravalli.example"), owner);
        return (db, partnerId, first, second, third);
    }

    [Fact]
    public async Task WithoutAChoice_TheFirstContactAddedIsPrimary()
    {
        var (db, partnerId, first, _, _) = await SeedAsync();
        await using var _db = db;

        var partner = await new IndustryPartnerService(db.NewContext()).GetAsync(partnerId);

        Assert.Equal(first, partner!.PrimaryContact!.Id);
        Assert.True(partner.Contacts[0].IsPrimary);
        Assert.Equal(1, partner.Contacts.Count(contact => contact.IsPrimary));
    }

    [Fact]
    public async Task MakePrimary_ShowsThatContactFirstEverywhere()
    {
        var (db, partnerId, first, second, third) = await SeedAsync();
        await using var _db = db;
        var service = new IndustryPartnerService(db.NewContext());

        await service.SetPrimaryContactAsync(partnerId, third, User("owner"));

        var reader = new IndustryPartnerService(db.NewContext());
        var partner = await reader.GetAsync(partnerId);
        Assert.Equal(new[] { third, first, second }, partner!.Contacts.Select(contact => contact.Id));
        Assert.True(partner.Contacts[0].IsPrimary);
        Assert.Equal(third, partner.PrimaryContact!.Id);

        var listed = Assert.Single((await reader.SearchAsync(null, IndustryPartnerDirectoryFilter.All, 1, 25)).Items);
        Assert.Equal("Col (Retd) Vikram Sethi, Director BD", listed.PrimaryContactName);
        Assert.Equal("+91 98110 24567", listed.ContactPhone);
        Assert.Equal("vikram@aravalli.example", listed.ContactEmail);

        var exported = Assert.Single(await reader.ExportDirectoryAsync(null, IndustryPartnerDirectoryFilter.All, IndustryPartnerDirectorySort.Name));
        Assert.Equal("Col (Retd) Vikram Sethi, Director BD", exported.PrimaryContact!.Name);
        Assert.Equal(new[] { "Col (Retd) Vikram Sethi, Director BD", "Business Development Office", "Ms Neha Kapoor" }, exported.Contacts.Select(contact => contact.Name));
    }

    [Fact]
    public async Task RemovingTheChosenPrimary_FallsBackToTheFirstContactAdded()
    {
        var (db, partnerId, first, second, _) = await SeedAsync();
        await using var _db = db;
        var service = new IndustryPartnerService(db.NewContext());
        await service.SetPrimaryContactAsync(partnerId, second, User("owner"));

        await new IndustryPartnerService(db.NewContext()).DeleteContactAsync(partnerId, second, User("owner"));

        await using var check = db.NewContext();
        Assert.Null((await check.IndustryPartners.SingleAsync(partner => partner.Id == partnerId)).PrimaryContactId);
        var partner = await new IndustryPartnerService(db.NewContext()).GetAsync(partnerId);
        Assert.Equal(first, partner!.PrimaryContact!.Id);
    }

    [Fact]
    public async Task MakePrimary_RejectsAContactFromAnotherOrganisation()
    {
        var (db, partnerId, _, _, _) = await SeedAsync();
        await using var _db = db;
        var otherPartner = await db.AddPartnerAsync("Other Org");
        var service = new IndustryPartnerService(db.NewContext());
        var foreignContact = await service.AddContactAsync(otherPartner, new ContactRequest("Someone", "111", null), User("owner"));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetPrimaryContactAsync(partnerId, foreignContact, User("owner")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetPrimaryContactAsync(99999, foreignContact, User("owner")));
    }

    [Fact]
    public async Task MakePrimary_WhenAnotherUserChangedTheOrganisationMeanwhile_KeepsTheChoice()
    {
        var (db, partnerId, _, second, _) = await SeedAsync();
        await using var _db = db;
        var first = db.NewContext();
        _ = await first.IndustryPartners.SingleAsync(partner => partner.Id == partnerId);

        // Another user adds a contact, changing the organisation's row version.
        await new IndustryPartnerService(db.NewContext()).AddContactAsync(partnerId, new ContactRequest("Late", "999", null), User("other"));

        await new IndustryPartnerService(first).SetPrimaryContactAsync(partnerId, second, User("owner"));

        await using var check = db.NewContext();
        Assert.Equal(second, (await check.IndustryPartners.SingleAsync(partner => partner.Id == partnerId)).PrimaryContactId);
    }

    [Fact]
    public async Task MakePrimaryHandler_IsLimitedToPeopleWhoCanEditTheOrganisation()
    {
        var (db, partnerId, _, second, _) = await SeedAsync();
        await using var _db = db;

        var stranger = CreatePage<IndexModel>(db, User("stranger"), allowPolicies: false);
        var refused = await stranger.OnPostMakePrimaryContactAsync(partnerId, second, CancellationToken.None);
        Assert.IsType<ForbidResult>(refused);

        var owner = CreatePage<IndexModel>(db, User("owner"), allowPolicies: false);
        var redirect = Assert.IsType<RedirectToPageResult>(await owner.OnPostMakePrimaryContactAsync(partnerId, second, CancellationToken.None));
        Assert.Equal("Primary contact updated.", owner.TempData["Message"]);
        Assert.Equal("./Index", redirect.PageName);
        Assert.Equal("contacts", redirect.RouteValues?["tab"]);
    }

    [Fact]
    public void VCard_EscapesSpecialCharactersAndFoldsLongLines()
    {
        var contacts = new[]
        {
            new IndustryPartnerContactDto(1, "Col (Retd) Vikram Sethi, Director BD; EW", "+91 98110 24567", "vikram@aravalli.example", null, DateTimeOffset.UtcNow, "AA=="),
            new IndustryPartnerContactDto(2, null, "0124 455 0100", null, null, DateTimeOffset.UtcNow, "AA==")
        };

        var card = IndustryPartnerVCard.Build("Aravalli Defence Systems Pvt Ltd, Gurugram — a very long organisation name for folding", contacts);

        Assert.Equal(2, CountOf(card, "BEGIN:VCARD\r\n"));
        Assert.Contains("FN:Col (Retd) Vikram Sethi\\, Director BD\\; EW\r\n", card);
        Assert.Contains("TEL;TYPE=WORK,VOICE:+91 98110 24567\r\n", card);
        Assert.Contains("EMAIL;TYPE=INTERNET:vikram@aravalli.example\r\n", card);
        Assert.Contains("FN:Aravalli Defence Systems Pvt Ltd\\, Gurugram", card); // unnamed contact uses the organisation
        foreach (var line in card.Split("\r\n"))
        {
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"Line longer than 75 octets: {line}");
        }

        // Unfolding (removing CRLF + space) restores the original value.
        Assert.Contains("ORG:Aravalli Defence Systems Pvt Ltd\\, Gurugram — a very long organisation name for folding", card.Replace("\r\n ", string.Empty));
    }

    [Theory]
    [InlineData("Aravalli Defence Systems Pvt Ltd", null, "aravalli-defence-systems-pvt-ltd-contacts.vcf")]
    [InlineData("M/s Shakti EW", "Mr H. S. Bedi, VP", "mr-h-s-bedi-vp-m-s-shakti-ew.vcf")]
    [InlineData("।।।", null, "contacts.vcf")]
    public void VCard_FileNamesAreSafe(string organisation, string? contact, string expected)
    {
        Assert.Equal(expected, IndustryPartnerVCard.FileName(organisation, contact));
    }

    [Fact]
    public async Task ContactCardHandler_ReturnsOneOrAllContacts_AndRefusesOtherOrganisationsContacts()
    {
        var (db, partnerId, _, second, _) = await SeedAsync();
        await using var _db = db;
        var other = await db.AddPartnerAsync("Other Org");
        var page = CreatePage<IndexModel>(db, User("viewer"));

        var one = Assert.IsType<FileContentResult>(await page.OnGetContactCardAsync(partnerId, second, CancellationToken.None));
        var all = Assert.IsType<FileContentResult>(await page.OnGetContactCardAsync(partnerId, null, CancellationToken.None));
        var wrong = await page.OnGetContactCardAsync(other, second, CancellationToken.None);

        Assert.Equal(IndustryPartnerVCard.ContentType, one.ContentType);
        Assert.Equal(1, CountOf(Encoding.UTF8.GetString(one.FileContents), "BEGIN:VCARD"));
        Assert.Equal(3, CountOf(Encoding.UTF8.GetString(all.FileContents), "BEGIN:VCARD"));
        Assert.Equal("aravalli-defence-contacts.vcf", all.FileDownloadName);
        Assert.IsType<NotFoundResult>(wrong);
    }

    [Fact]
    public async Task OrganisationPage_LoadsOneOrganisation_WithoutTheList()
    {
        var (db, partnerId, _, _, _) = await SeedAsync();
        await using var _db = db;
        var page = CreatePage<OrganisationModel>(db, User("viewer"));
        page.Id = partnerId;

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal("Aravalli Defence", page.SelectedPartner!.Name);
        Assert.Empty(page.SearchResult.Items);
        Assert.Equal(StatusCodes.Status200OK, page.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task OrganisationPage_UnknownOrganisation_IsNotFound()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var page = CreatePage<OrganisationModel>(db, User("viewer"));
        page.Id = 4242;

        await page.OnGetAsync(CancellationToken.None);

        Assert.Null(page.SelectedPartner);
        Assert.Equal(StatusCodes.Status404NotFound, page.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task OrganisationPage_ActionsReturnToThePage_AndDeleteReturnsToTheList()
    {
        var (db, partnerId, _, second, _) = await SeedAsync();
        await using var _db = db;
        var owner = CreatePage<OrganisationModel>(db, User("owner", RoleNames.Admin));

        var afterPrimary = Assert.IsType<RedirectToPageResult>(await owner.OnPostMakePrimaryContactAsync(partnerId, second, CancellationToken.None));
        Assert.Equal("./Organisation", afterPrimary.PageName);
        Assert.Equal(partnerId, afterPrimary.RouteValues?["id"]);

        var empty = await db.AddPartnerAsync("Empty Org");
        var afterDelete = Assert.IsType<RedirectToPageResult>(await owner.OnPostDeletePartnerAsync(empty, CancellationToken.None));
        Assert.Equal("./Index", afterDelete.PageName);
        Assert.Null(afterDelete.RouteValues?["id"]);
    }

    // SECTION: Helpers
    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty).Length) / value.Length;

    private static T CreatePage<T>(IndustryPartnerTestDb db, ClaimsPrincipal user, bool allowPolicies = true)
        where T : IndexModel
    {
        var service = new IndustryPartnerService(db.NewContext());
        IAuthorizationService authorization = allowPolicies ? new FixedAuthorizationService(true) : new FixedAuthorizationService(false);
        var page = (T)Activator.CreateInstance(typeof(T), service, new UnusedAttachmentManager(), authorization)!;
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        return page;
    }

    private sealed class FixedAuthorizationService(bool allow) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private sealed class UnusedAttachmentManager : IIndustryPartnerAttachmentManager
    {
        public Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(System.IO.Stream Stream, string FileName, string ContentType)> DownloadAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(int partnerId, Guid attachmentId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
